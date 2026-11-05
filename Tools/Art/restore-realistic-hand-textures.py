"""Recover original hand maps and level baked UV-island lighting, preserving skin detail.

Run after geometry authoring:
Blender --background --python Tools/Art/restore-realistic-hand-textures.py
Only texture PNGs are exported. Meshes, UVs, rig, materials and metadata are read-only.
"""

from pathlib import Path
import hashlib
import json
import subprocess

import bpy
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
REVISION = "fa84b375ba78cce06bcde7e04960cae5fff6dd94"
TEXTURES = ROOT / "Assets/HalliGalliMovement/Art/Textures"
BACKUP = ROOT / "Build/RealisticHandTextureRecovery" / REVISION
PREVIEWS = ROOT / "Previews/RealisticHands"
SOURCE_HASHES = {
    "Hands_BaseColor.png": "6f80dddc21e95fc6be0bbbf87110d4db1f5a7980aba765cc471f0c4b30d56e93",
    "Hands_Normal.png": "9d635534e475c7dc60f869767dbd9b0f37a94ddd7aa9a7b98642a6bd4febe8bc",
    "Hands_ORM.png": "405795d930d4468305f9a9e12963e6b3846cd02348037040bc9459f5f3de220b",
    "Hands_AO.png": "c534dd3894810cd509e3e777af0ec2cd86c8022dd44442b082384d122d790d4e",
    "Hands_MetalSmooth.png": "bfe8fb2d614515018eb8ca1cc773f38ec9a8c851e472d9df5f3736e27335dfec",
}
LOW_FREQUENCY_SIGMA = 12.0
COLOR_DIFFUSION_STEPS = 100
COLOR_DIFFUSION_RATE = 0.6
PADDING_PIXELS = 8


def original_bytes(name):
    path = BACKUP / name
    if path.exists():
        data = path.read_bytes()
    else:
        asset = f"Assets/HalliGalliMovement/Art/Textures/{name}"
        data = subprocess.run(["git", "show", f"{REVISION}:{asset}"], cwd=ROOT, capture_output=True, check=True).stdout
        if data.startswith(b"version https://git-lfs.github.com/spec/v1"):
            data = subprocess.run(["git", "lfs", "smudge"], cwd=ROOT, input=data, capture_output=True, check=True).stdout
        if hashlib.sha256(data).hexdigest() != SOURCE_HASHES[name]:
            raise RuntimeError(f"Original Git/LFS texture could not be recovered: {name}")
        path.write_bytes(data)
    if hashlib.sha256(data).hexdigest() != SOURCE_HASHES[name]:
        raise RuntimeError(f"Original texture backup was modified: {name}")
    return data


def gaussian(field, sigma):
    padding = int(np.ceil(sigma * 4))
    padded = np.pad(field, padding, mode="reflect")
    height, width = padded.shape
    fy = np.fft.fftfreq(height)[:, None]
    fx = np.fft.rfftfreq(width)[None, :]
    kernel = np.exp(-2 * np.pi ** 2 * sigma ** 2 * (fy * fy + fx * fx))
    filtered = np.fft.irfft2(np.fft.rfft2(padded) * kernel, s=padded.shape)
    return filtered[padding:-padding, padding:-padding].astype(np.float32)


def sample(image, uv):
    height, width = image.shape[:2]
    xy = np.clip(uv * (width, height) - 0.5, (0, 0), (width - 1.001, height - 1.001))
    lo = np.floor(xy).astype(int)
    t = xy - lo
    x, y = lo.T
    return ((image[y, x] * (1 - t[:, 0, None]) + image[y, x + 1] * t[:, 0, None]) * (1 - t[:, 1, None])
            + (image[y + 1, x] * (1 - t[:, 0, None]) + image[y + 1, x + 1] * t[:, 0, None]) * t[:, 1, None])


def mesh_color_field(low):
    # Read the geometry owner's authoring mesh without changing or saving it.
    with bpy.data.libraries.load(str(ROOT / "ArtSource/Hands/RealisticHands.blend"), link=False) as (_, data):
        data.objects = ["Realistic_Hand_L"]
    obj = data.objects[0]
    mesh = obj.data
    mesh.calc_loop_triangles()
    positions = np.asarray([vertex.co[:] for vertex in mesh.vertices], dtype=np.float32)
    _, welded = np.unique(np.round(positions, 6), axis=0, return_inverse=True)
    triangles = np.asarray([triangle.vertices[:] for triangle in mesh.loop_triangles], dtype=np.int32)
    loop_indices = np.asarray([triangle.loops[:] for triangle in mesh.loop_triangles], dtype=np.int32)
    uv = np.asarray([loop.uv[:] for loop in mesh.uv_layers.active.data], dtype=np.float32)[loop_indices]
    corner_colors = sample(low, uv.reshape(-1, 2)).reshape(-1, 3, 3)
    corners = welded[triangles]
    vertex_count = int(welded.max()) + 1
    counts = np.bincount(corners.ravel(), minlength=vertex_count)
    colors = np.stack([
        np.bincount(corners.ravel(), weights=corner_colors[:, :, channel].ravel(), minlength=vertex_count)
        / np.maximum(counts, 1) for channel in range(3)
    ], axis=1)
    edges = np.concatenate((corners[:, [0, 1]], corners[:, [1, 2]], corners[:, [2, 0]]))
    edges = np.unique(np.sort(edges, axis=1), axis=0)
    edges = edges[edges[:, 0] != edges[:, 1]]
    sources = np.concatenate((edges[:, 0], edges[:, 1]))
    destinations = np.concatenate((edges[:, 1], edges[:, 0]))
    degree = np.bincount(destinations, minlength=vertex_count)
    # Diffuse only the broad COLOR field over the actual continuous hand surface.
    # Source texture pixels are never blurred, and UV-separated copies of the
    # same anatomical vertex share one target color rather than a hard seam.
    for _ in range(COLOR_DIFFUSION_STEPS):
        neighbor = np.stack([
            np.bincount(destinations, weights=colors[sources, channel], minlength=vertex_count)
            / np.maximum(degree, 1) for channel in range(3)
        ], axis=1)
        colors += COLOR_DIFFUSION_RATE * (neighbor - colors)
    delta = colors[corners] - corner_colors
    bpy.data.objects.remove(obj)
    return uv, delta.astype(np.float32), {"weldedColorVertices": vertex_count, "triangles": int(len(triangles))}


def rasterize(uv, delta, height, width, skin):
    field = np.zeros((height, width, 3), dtype=np.float32)
    covered = np.zeros((height, width), dtype=bool)
    for points, values in zip(uv * (width, height) - 0.5, delta):
        x0, y0 = np.maximum(np.floor(points.min(axis=0)).astype(int), 0)
        x1, y1 = np.minimum(np.ceil(points.max(axis=0)).astype(int) + 1, (width, height))
        if x1 <= x0 or y1 <= y0:
            continue
        a, b, c = points
        divisor = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
        if abs(divisor) < 1e-8:
            continue
        yy, xx = np.mgrid[y0:y1, x0:x1]
        wa = ((b[1] - c[1]) * (xx - c[0]) + (c[0] - b[0]) * (yy - c[1])) / divisor
        wb = ((c[1] - a[1]) * (xx - c[0]) + (a[0] - c[0]) * (yy - c[1])) / divisor
        wc = 1 - wa - wb
        inside = (wa >= -1e-5) & (wb >= -1e-5) & (wc >= -1e-5)
        tile = wa[:, :, None] * values[0] + wb[:, :, None] * values[1] + wc[:, :, None] * values[2]
        field[y0:y1, x0:x1][inside] = tile[inside]
        covered[y0:y1, x0:x1] |= inside
    # Extend only the correction into existing source atlas padding. Zeroing at a
    # UV border preserves the original bad color step; matched continuation does not.
    for _ in range(PADDING_PIXELS):
        total = np.zeros_like(field)
        count = np.zeros_like(covered, dtype=np.uint8)
        for target, source in [((slice(1, None), slice(None)), (slice(None, -1), slice(None))),
                               ((slice(None, -1), slice(None)), (slice(1, None), slice(None))),
                               ((slice(None), slice(1, None)), (slice(None), slice(None, -1))),
                               ((slice(None), slice(None, -1)), (slice(None), slice(1, None)))]:
            total[target] += field[source]
            count[target] += covered[source]
        fill = ~covered & (count > 0) & skin
        field[fill] = total[fill] / count[fill, None]
        covered |= fill
    field[~skin] = 0
    return field, covered


def correct_basecolor():
    image = bpy.data.images.load(str(BACKUP / "Hands_BaseColor.png"), check_existing=False)
    image.colorspace_settings.name = "Non-Color"
    width, height = image.size
    if (width, height) != (4096, 4096):
        raise RuntimeError("The original 4096-square UV atlas is required")
    pixels = np.empty(width * height * 4, dtype=np.float32)
    image.pixels.foreach_get(pixels)
    pixels = pixels.reshape(height, width, 4)
    rgb = pixels[:, :, :3]
    skin = np.max(rgb, axis=2) > 0.04
    occupied = skin.astype(np.float32)
    denominator = gaussian(occupied, LOW_FREQUENCY_SIGMA)
    low = np.stack([
        gaussian(rgb[:, :, channel] * occupied, LOW_FREQUENCY_SIGMA) / np.maximum(denominator, 1e-6)
        for channel in range(3)
    ], axis=2)
    uv, delta, summary = mesh_color_field(low)
    del low, denominator
    field, covered = rasterize(uv, delta, height, width, skin)
    summary["sourceMeanRGB"] = rgb[skin].mean(axis=0, dtype=np.float64).tolist()
    rgb += field
    summary["clippedSkinPixels"] = int(np.count_nonzero(skin & np.any((rgb < 0) | (rgb > 1), axis=2)))
    np.clip(rgb, 0, 1, out=rgb)
    summary["correctedMeanRGB"] = rgb[skin].mean(axis=0, dtype=np.float64).tolist()
    summary["coveredSkinPixels"] = int(np.count_nonzero(covered & skin))
    summary["skinPixels"] = int(np.count_nonzero(skin))
    summary["maximumAbsoluteRGBOffset"] = np.abs(field).max(axis=(0, 1)).tolist()
    output = bpy.data.images.new("RealisticHandBaseColor", width=width, height=height, alpha=True)
    output.colorspace_settings.name = "Non-Color"
    output.pixels.foreach_set(pixels.ravel())
    output.filepath_raw = str(TEXTURES / "Hands_BaseColor.png")
    output.file_format = "PNG"
    output.save()
    bpy.data.images.remove(output)
    bpy.data.images.remove(image)
    return summary


def main():
    BACKUP.mkdir(parents=True, exist_ok=True)
    PREVIEWS.mkdir(parents=True, exist_ok=True)
    originals = {name: original_bytes(name) for name in SOURCE_HASHES}
    for name, data in originals.items():
        if name != "Hands_BaseColor.png":
            target = TEXTURES / name
            if not target.exists() or target.read_bytes() != data:
                target.write_bytes(data)
    summary = correct_basecolor()
    report = {
        "sourceRevision": REVISION,
        "sourceHashes": SOURCE_HASHES,
        "backupDirectory": str(BACKUP.relative_to(ROOT)).replace("\\", "/"),
        "method": "Topology-aware low-frequency UV color-seam leveling. Only a broad correction field is rasterized onto the original atlas; original source pixel residual pores/creases are retained. No synthetic texture, UV change, source-RGB blur, global whitening or geometry edit.",
        "parameters": {"sourceLowFrequencySigmaPixels": LOW_FREQUENCY_SIGMA, "colorDiffusionSteps": COLOR_DIFFUSION_STEPS,
                       "colorDiffusionRate": COLOR_DIFFUSION_RATE, "atlasPaddingPixels": PADDING_PIXELS,
                       "readOnlyGeometry": "ArtSource/Hands/RealisticHands.blend"},
        "effect": summary,
        "supportMaps": "Normal, ORM, AO and MetalSmooth are byte-identical original Git/LFS maps. Metadata untouched.",
        "baseColorSha256": hashlib.sha256((TEXTURES / "Hands_BaseColor.png").read_bytes()).hexdigest(),
        "scope": "Texture authoring and appearance preview only; no game validation, build, material, mesh, or scene edit.",
    }
    (PREVIEWS / "skin-shadow-correction.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
