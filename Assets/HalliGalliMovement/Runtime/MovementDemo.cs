using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace HalliGalli.Movement
{
    public sealed class MovementDemo : MonoBehaviour
    {
        public SourcePlayerController player;
        public Vector3 spawn = new Vector3(0, 0.08f, -3);
        public float respawnBelow = -5;
        private GUIStyle title, text, badge;
        private Font font;
        private bool korean;

        private void Update()
        {
            if (player == null || ControlSettings.BlocksInput) return;
            bool reset = player.PointerActionsAllowed && ControlBindings.Pressed(GameAction.Respawn);
#if ENABLE_INPUT_SYSTEM
            bool testPush=Keyboard.current!=null && Keyboard.current.hKey.wasPressedThisFrame;
            bool testKick=Keyboard.current!=null && Keyboard.current.jKey.wasPressedThisFrame;
#else
            bool testPush=Input.GetKeyDown(KeyCode.H),testKick=Input.GetKeyDown(KeyCode.J);
#endif
            if(Cursor.lockState==CursorLockMode.Locked && (testPush||testKick))
                player.GetComponent<MeleeReceiver>()?.Receive(testKick?MeleeAttack.Kick:MeleeAttack.PushLeft,-player.transform.forward,player.Motor.CurrentEyePosition);
            if (reset || player.transform.position.y < respawnBelow)
            {
                player.Motor.Teleport(spawn);
                player.ResetView();
                player.GetComponent<MeleeCombat>()?.ResetCombat();
                player.GetComponent<MeleeReceiver>()?.ResetReaction();
                foreach(var target in FindObjectsByType<TrainingOpponent>())target.ResetTarget();
            }
        }

        private void OnGUI()
        {
            if (player == null || Application.isBatchMode) return;
            if (title == null)
            {
                korean = System.Array.Exists(Font.GetOSInstalledFontNames(), n => n == "Malgun Gothic" || n == "맑은 고딕");
                if (korean) font = Font.CreateDynamicFontFromOSFont("Malgun Gothic", 18);
                title = new GUIStyle(GUI.skin.label) { font = font, fontSize = 26, fontStyle = FontStyle.Bold };
                text = new GUIStyle(GUI.skin.label) { font = font, fontSize = 16 };
                badge = new GUIStyle(text) { alignment = TextAnchor.MiddleRight };
            }
            GUI.color = new Color(0.04f, 0.07f, 0.1f, 0.9f);
            GUI.DrawTexture(new Rect(20, 20, 650, 192), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(38, 30, 610, 38), korean ? "할리갈리 데스런 · 이동 / 타격 테스트" : "HALLI GALLI / MOVEMENT & COMBAT", title);
            GUI.Label(new Rect(38, 75, 540, 25), korean ? "WASD 이동   Space 점프   Ctrl 웅크리기" : "WASD move   Space jump   Ctrl crouch", text);
            GUI.Label(new Rect(38, 107, 540, 25), korean ? "마우스 시점   V 전신 확인   R 처음으로   Esc 커서 해제" : "Mouse look   V body view   R restart   Esc cursor", text);
            GUI.Label(new Rect(38, 139, 610, 25), korean ? "좌클릭 한 손 밀치기   F 발차기   연습 상대는 3초 뒤 복귀" : "LMB one-hand push   F kick   Target returns after 3 s", text);
            GUI.Label(new Rect(38, 171, 610, 25), korean ? "수평 밀치기 · 피격 중에도 이동/반격 가능 · H / J 피격 테스트" : "Horizontal push · move and counterattack freely · H / J test hits", text);
            string state = !player.Motor.Grounded ? (korean ? "공중" : "AIR") : player.Motor.IsCrouched ? (korean ? "웅크리기" : "CROUCH") : (korean ? "지상" : "GROUND");
            GUI.Label(new Rect(Screen.width - 300, 25, 270, 30), state + "  |  " + player.Motor.HorizontalSpeed.ToString("0.0") + " m/s", badge);
            GUI.color = new Color(1, 1, 1, 0.85f);
            GUI.DrawTexture(new Rect(Screen.width / 2f - 2, Screen.height / 2f - 2, 4, 4), Texture2D.whiteTexture);
            var combat=player.GetComponent<MeleeCombat>();
            if(combat && combat.HitMarkerRemaining>0) {
                GUI.color=new Color(1,.62f,.22f);
                GUI.DrawTexture(new Rect(Screen.width/2f-12,Screen.height/2f-1,24,2),Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(Screen.width/2f-1,Screen.height/2f-12,2,24),Texture2D.whiteTexture);
            }
            GUI.color=Color.white;
            if (Cursor.lockState != CursorLockMode.Locked)
                GUI.Label(new Rect(38, Screen.height - 54, 650, 30), korean ? "화면을 클릭하면 조작을 시작합니다." : "Click the screen to start.", text);
        }
    }
}
