using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using HalliGalli.Movement;
using Object = UnityEngine.Object;

namespace HalliGalli.EditorTools
{
    // Copies the saved start scene once; never calls an official map or asset builder.
    public static class UserMapTemplateBuilder
    {
        [MenuItem("HalliGalli/Maps/Build User Map Template Only")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before building the user-map template.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(CourseBuilder.ScenePath);
            if (!EditorSceneManager.SaveScene(scene, UserMapRuntime.ScenePath)) throw new InvalidOperationException("Could not create the user-map template copy.");
            var session = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<CourseSession>(true)).Single();
            var roster = session.roster;
            if (!roster || !session.maps || !session.checkpoints || !session.bellTarget || !session.bellButton || !session.bellAudio)
                throw new InvalidOperationException("The saved Bell Run scene is missing its roster, map controller, checkpoints or bell assets.");

            TutorialFerryBuilder.RemoveFromTemplate(session);
            var keep = new HashSet<GameObject>();
            Action<Component> preserve = component => { if (component) keep.Add(component.transform.root.gameObject); };
            preserve(session); preserve(roster); preserve(session.maps); preserve(session.checkpoints);
            preserve(session.cardGame); preserve(session.cardGame.display); preserve(session.cardGame.tableView); preserve(session.bellTarget);
            foreach (var participant in roster.allParticipants) {
                preserve(participant); preserve(participant.seatAnchor); preserve(participant.standAnchor); preserve(participant.retryAnchor);
                var bot = participant.GetComponent<CourseRival>(); if (bot) bot.route = Array.Empty<Transform>();
            }
            foreach (var chair in roster.chairs) preserve(chair);
            foreach (var route in roster.routeRoots) {
                preserve(route);
                foreach (var child in route.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
            }
            foreach (var hud in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<CourseHUD>(true))) preserve(hud);

            // Preserve the actual table/deck and the imported CardTable visual, not a reconstructed imitation.
            foreach (var root in scene.GetRootGameObjects()) {
                if (root.name == "VARCO environment") {
                    foreach (var child in root.transform.Cast<Transform>().ToArray())
                        if (child.name != "CardTable") Object.DestroyImmediate(child.gameObject);
                    keep.Add(root);
                }
                if (root.name == "Table deck" || root.name == "Table top" || root.name == "Table felt" || root.name == "Table leg" || root.name == "START"
                    || (root.name == "Edge stripe" || root.name == "Platform support") && root.transform.position.z <= .01f)
                    keep.Add(root);
                var light = root.GetComponent<Light>();
                if (light && light.type == LightType.Directional) keep.Add(root);
            }
            foreach (var root in scene.GetRootGameObjects()) if (!keep.Contains(root)) Object.DestroyImmediate(root);

            // Roster still lays out six seats. Empty geometry anchors retain its serialized seat-array contract.
            for (int i = 0; i < roster.firstPlatforms.Length; i++) {
                Transform parent = roster.firstPlatforms[i].parent;
                Object.DestroyImmediate(roster.firstPlatforms[i].gameObject);
                roster.firstPlatforms[i] = Anchor(parent, "User-map unused lane " + i);
                roster.firstPlatforms[i].gameObject.SetActive(false);
            }
            Transform mergeParent = roster.mergeSurface.parent.parent;
            Object.DestroyImmediate(roster.mergeSurface.parent.gameObject);
            var merge = Anchor(mergeParent, "User-map unused merge anchors");
            roster.mergeSurface = Anchor(merge, "Surface"); merge.gameObject.SetActive(false);
            foreach (var child in session.checkpoints.transform.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
            session.checkpoints.points = Array.Empty<CourseCheckpoints.Point>();
            session.factory = null; session.ferry = null; roster.branches = null; roster.surf = null;

            var runtime = new GameObject("User map runtime").AddComponent<UserMapRuntime>();
            runtime.session = session; session.userMap = runtime;
            runtime.movementProfile = AssetDatabase.LoadAssetAtPath<SourceMovementProfile>(MovementProjectBuilder.Root + "/Settings/DefaultMovement.asset");
            if (!runtime.movementProfile) throw new InvalidOperationException("The original movement profile is missing.");
            var template = session.bellTarget.gameObject;
            template.name = "Original service bell template"; template.transform.SetParent(runtime.transform, false);
            template.transform.localPosition = Vector3.zero; template.transform.localRotation = Quaternion.identity; template.transform.localScale = Vector3.one;
            template.SetActive(false); runtime.bellVisualTemplate = template;
            session.fallHeight = -20;
            RenderSettings.fogStartDistance = 100; RenderSettings.fogEndDistance = 450;
            RenderSettings.ambientLight = new Color(.42f, .48f, .55f);
            foreach (var root in scene.GetRootGameObjects()) {
                var light = root.GetComponent<Light>();
                if (light && light.type == LightType.Directional) light.intensity = light.name == "Key" ? 1.05f : .35f;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save the user-map template.");
            // Preserve unrelated build entries and enable/add only this dedicated scene.
            var scenes = EditorBuildSettings.scenes.ToList();
            var entry = scenes.FindIndex(item => item.path == UserMapRuntime.ScenePath);
            if (entry >= 0) scenes[entry] = new EditorBuildSettingsScene(UserMapRuntime.ScenePath, true);
            else scenes.Add(new EditorBuildSettingsScene(UserMapRuntime.ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("USER_MAP_TEMPLATE_CREATED " + UserMapRuntime.ScenePath);
        }
        static Transform Anchor(Transform parent, string name)
        {
            var anchor = new GameObject(name).transform; anchor.SetParent(parent, false); return anchor;
        }
    }
}
