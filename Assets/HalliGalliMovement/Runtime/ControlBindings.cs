using System;
using System.Linq;
using UnityEngine;

namespace HalliGalli.Movement
{
    public enum GameAction { Forward, Back, Left, Right, Jump, Crouch, Primary, Kick, Lasso, Interact, Respawn, BodyView, UseItem }

    // The project uses Unity's legacy input backend. Store physical controls, never translated labels.
    public static class ControlBindings
    {
        public const int WheelUp=-1, WheelDown=-2, Slots=3;
        public const string PreferenceKey="HalliGalli.Controls.v1";
        public static readonly GameAction[] Actions=(GameAction[])Enum.GetValues(typeof(GameAction));
        public static readonly string[] Names={"앞으로","뒤로","왼쪽으로","오른쪽으로","점프","웅크리기","밀치기 / 카드 공개","발차기","밧줄 · 자동 당김","일어나기 / 앉기 / 종","체크포인트 복귀","전신 보기","레이스 아이템 사용"};
        static readonly KeyCode[] keys=((KeyCode[])Enum.GetValues(typeof(KeyCode))).Distinct().Where(k=>(int)k>0&&(int)k<(int)KeyCode.JoystickButton0).ToArray();
        static int[][] bindings;
        [Serializable] sealed class Saved { public int version=1; public Row[] rows; }
        [Serializable] sealed class Row { public int action; public int[] codes; }
        public static int Get(GameAction action,int slot){Ensure();return bindings[(int)action][slot];}
        public static string Hint(GameAction action){Ensure();return Label(bindings[(int)action].FirstOrDefault(c=>c!=0));}
        public static bool AllowsWheel(GameAction action)=>(int)action>=4&&action!=GameAction.Crouch;
        public static bool Valid(int code)=>code==0||code==WheelUp||code==WheelDown||keys.Contains((KeyCode)code)&&code!=(int)KeyCode.Escape;
        public static string Label(int code)
        {
            if(code==0)return "—";if(code==WheelUp)return "휠 ↑";if(code==WheelDown)return "휠 ↓";
            var key=(KeyCode)code;
            switch(key){case KeyCode.Space:return "SPACE";case KeyCode.LeftControl:return "L CTRL";case KeyCode.RightControl:return "R CTRL";
                case KeyCode.LeftShift:return "L SHIFT";case KeyCode.RightShift:return "R SHIFT";case KeyCode.LeftAlt:return "L ALT";case KeyCode.RightAlt:return "R ALT";
                case KeyCode.Mouse0:return "LMB";case KeyCode.Mouse1:return "RMB";case KeyCode.Mouse2:return "MMB";case KeyCode.Mouse3:return "MOUSE 4";case KeyCode.Mouse4:return "MOUSE 5";
                case KeyCode.UpArrow:return "↑";case KeyCode.DownArrow:return "↓";case KeyCode.LeftArrow:return "←";case KeyCode.RightArrow:return "→";}
            string name=key.ToString();return (name.StartsWith("Alpha")?name.Substring(5):name).ToUpperInvariant();
        }
        public static void Defaults(bool save=true)
        {
            bindings=Actions.Select(a=>new int[Slots]).ToArray();
            var defaults=new[]{KeyCode.W,KeyCode.S,KeyCode.A,KeyCode.D,KeyCode.Space,KeyCode.LeftControl,KeyCode.Mouse0,KeyCode.F,KeyCode.Q,KeyCode.E,KeyCode.R,KeyCode.V,KeyCode.C};
            for(int i=0;i<defaults.Length;i++)bindings[i][0]=(int)defaults[i];
            bindings[(int)GameAction.Jump][1]=WheelDown;
            bindings[(int)GameAction.Crouch][1]=(int)KeyCode.RightControl;
            if(save)Save();
        }
        static void Ensure(){if(bindings==null)Reload();}
        public static void Reload()
        {
            Defaults(false);
            try{
                var saved=JsonUtility.FromJson<Saved>(PlayerPrefs.GetString(PreferenceKey,""));
                if(saved==null||saved.version!=1||saved.rows==null)return;
                var candidate=bindings.Select(row=>(int[])row.Clone()).ToArray();var seen=new System.Collections.Generic.HashSet<int>();
                if(saved.rows.Length!=Actions.Length&&saved.rows.Length!=(int)GameAction.UseItem)return;
                for(int i=0;i<saved.rows.Length;i++){
                    var row=saved.rows[i];if(row==null||row.action!=i||row.codes==null||row.codes.Length!=Slots)return;
                    for(int s=0;s<Slots;s++){
                        int code=row.codes[s];if(!Valid(code)||code<0&&!AllowsWheel((GameAction)i)||code!=0&&!seen.Add(code))return;
                        candidate[i][s]=code;
                    }
                }
                // Preserve existing custom controls; never steal a key for the newly added action.
                for(int i=saved.rows.Length;i<Actions.Length;i++)
                    for(int s=0;s<Slots;s++)if(candidate[i][s]!=0&&!seen.Add(candidate[i][s]))candidate[i][s]=0;
                bindings=candidate;
            }catch(ArgumentException){ }
        }
        public static void Save(){Ensure();PlayerPrefs.SetString(PreferenceKey,JsonUtility.ToJson(new Saved{rows=Actions.Select(a=>new Row{action=(int)a,codes=bindings[(int)a]}).ToArray()}));PlayerPrefs.Save();}
        public static bool Conflict(GameAction action,int slot,int code,out GameAction other,out int otherSlot)
        {
            Ensure();foreach(var a in Actions)for(int s=0;s<Slots;s++)if(code!=0&&(a!=action||s!=slot)&&bindings[(int)a][s]==code){other=a;otherSlot=s;return true;}
            other=default;otherSlot=-1;return false;
        }
        public static bool Assign(GameAction action,int slot,int code,bool swap=false,bool save=true)
        {
            Ensure();if(slot<0||slot>=Slots||!Valid(code)||code<0&&!AllowsWheel(action))return false;
            if(Conflict(action,slot,code,out var other,out int otherSlot)){
                if(!swap)return false;
                int prior=bindings[(int)action][slot];
                if(prior<0&&!AllowsWheel(other))return false;
                bindings[(int)other][otherSlot]=prior;
            }
            bindings[(int)action][slot]=code;if(save)Save();return true;
        }
        public static bool Pressed(GameAction action)=>Read(action,true);
        public static bool Held(GameAction action)=>Read(action,false);
        static bool Read(GameAction action,bool pressed)
        {
            if(ControlSettings.BlocksInput)return false;
            Ensure();return Evaluate(action,pressed,Input.GetKey,Input.GetKeyDown,Input.mouseScrollDelta.y);
        }
        // Shared by the live sampler and deterministic input tests. Wheel is always a pulse, never held.
        public static bool Evaluate(GameAction action,bool pressed,Func<KeyCode,bool> held,Func<KeyCode,bool> down,float wheel)
        {
            Ensure();foreach(int code in bindings[(int)action]){
                if(code==WheelUp&&pressed&&wheel>0||code==WheelDown&&pressed&&wheel<0)return true;
                if(code>0&&(pressed?down((KeyCode)code):held((KeyCode)code)))return true;
            }return false;
        }
        public static Vector2 Movement=>new Vector2((Held(GameAction.Right)?1:0)-(Held(GameAction.Left)?1:0),(Held(GameAction.Forward)?1:0)-(Held(GameAction.Back)?1:0));
        public static int CaptureCode()
        {
            if(Input.mouseScrollDelta.y>0)return WheelUp;if(Input.mouseScrollDelta.y<0)return WheelDown;
            foreach(var key in keys)if(Input.GetKeyDown(key))return (int)key;return 0;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]static void Reset(){bindings=null;}
    }
}
