using System.Collections.Generic;
using UnityEngine;

namespace HalliGalli.Movement
{
    // Shared, resolution-independent skin. Icons are original vector strokes;
    // map thumbnails come from the game's own scenes.
    public static class BellUI
    {
        public static readonly Color Ink=new Color(.07f,.075f,.072f),Surface=new Color(.115f,.12f,.112f,.96f);
        public static readonly Color Paper=new Color(.94f,.92f,.85f),Muted=new Color(.64f,.66f,.61f),Gold=new Color(.91f,.69f,.34f);
        public static readonly Color Danger=new Color(1,.31f,.23f),Cool=new Color(.43f,.8f,.76f);
        static readonly Dictionary<int,GUIStyle> styles=new Dictionary<int,GUIStyle>();
        static Font font;static GUIStyle button,field;static readonly Dictionary<string,Texture2D> maps=new Dictionary<string,Texture2D>();
        public static float Scale=>Mathf.Min(Screen.width/1280f,Screen.height/720f);
        public static float W=>Screen.width/Scale;
        public static float H=>Screen.height/Scale;
        public static Vector2 MousePoint=>new Vector2(Input.mousePosition.x,Screen.height-Input.mousePosition.y)/Scale;
        public static void Begin(){GUI.matrix=Matrix4x4.Scale(Vector3.one*Scale);GUI.color=Color.white;}
        public static GUIStyle Style(int size=18,bool bold=false,TextAnchor align=TextAnchor.MiddleLeft)
        {
            int key=size*100+(bold?50:0)+(int)align;
            if(!styles.TryGetValue(key,out var style)){
                if(!font)font=Font.CreateDynamicFontFromOSFont(new[]{"Malgun Gothic","Arial"},20);
                style=new GUIStyle(GUI.skin.label){font=font,fontSize=size,fontStyle=bold?FontStyle.Bold:FontStyle.Normal,
                    alignment=align,richText=false,wordWrap=true,padding=new RectOffset(0,0,0,0),normal={textColor=Color.white}};styles.Add(key,style);
            }
            return style;
        }
        public static void Text(Rect rect,string text,int size=18,Color? color=null,bool bold=false,TextAnchor align=TextAnchor.MiddleLeft,bool shadow=false)
        {
            var old=GUI.color;var contentColor=GUI.contentColor;bool enabled=GUI.enabled;
            var style=Style(size,bold,align);var textColor=style.normal.textColor;
            // Labels carry their own color; Unity's disabled tint must not dim it again.
            // Buttons choose their muted label explicitly while their hit target stays disabled.
            GUI.color=Color.white;GUI.contentColor=Color.white;GUI.enabled=true;
            try{
                if(shadow){style.normal.textColor=new Color(0,0,0,.75f);GUI.Label(new Rect(rect.x+1,rect.y+2,rect.width,rect.height),text,style);}
                style.normal.textColor=color??Paper;GUI.Label(rect,text,style);
            }
            finally{style.normal.textColor=textColor;GUI.enabled=enabled;GUI.contentColor=contentColor;GUI.color=old;}
        }
        public static void Round(Rect rect,Color color,float radius=18,float border=0)
        {
            var old=GUI.color;GUI.color=Color.white;
            GUI.DrawTexture(rect,Texture2D.whiteTexture,ScaleMode.StretchToFill,true,0,color,border,Mathf.Min(radius,Mathf.Min(rect.width,rect.height)*.5f));GUI.color=old;
        }
        public static bool Button(Rect rect,string text,bool primary=false,bool selected=false,int size=17)
        {
            if(button==null)button=new GUIStyle(GUIStyle.none);
            bool hover=GUI.enabled&&rect.Contains(Event.current.mousePosition);
            Color fill=primary?Gold:selected?new Color(.27f,.24f,.18f):hover?new Color(.24f,.25f,.23f):new Color(.18f,.19f,.17f);
            if(!GUI.enabled)fill=new Color(.13f,.14f,.13f);
            Round(rect,fill,rect.height*.5f);
            if(selected)Round(rect,Gold,rect.height*.5f,1);
            bool clicked=GUI.Button(rect,GUIContent.none,button);
            Text(rect,text,size,!GUI.enabled?Muted:primary?Ink:Paper,primary,TextAnchor.MiddleCenter);
            return clicked;
        }
        public static string Field(Rect rect,string value,int maxLength,string controlName)
        {
            if(field==null){field=new GUIStyle(Style(23)){padding=new RectOffset(17,17,8,8)};field.normal.background=null;field.focused.background=null;field.normal.textColor=Paper;field.focused.textColor=Paper;}
            Round(rect,new Color(.055f,.06f,.052f),14);
            Round(rect,GUI.GetNameOfFocusedControl()==controlName?Gold:new Color(.3f,.32f,.28f),14,1);
            GUI.SetNextControlName(controlName);return GUI.TextField(rect,value,maxLength,field);
        }
        public static void Key(Rect rect,string text)
        {
            Round(rect,new Color(.12f,.13f,.115f,.86f),8);Round(rect,new Color(.6f,.62f,.55f,.35f),8,1);
            Text(rect,text,12,Paper,false,TextAnchor.MiddleCenter);
        }
        public static void Line(Vector2 a,Vector2 b,Color c,float width=2)
        {
            var old=GUI.matrix;Vector2 d=b-a;
            GUI.matrix=old*Matrix4x4.TRS(a,Quaternion.Euler(0,0,Mathf.Atan2(d.y,d.x)*Mathf.Rad2Deg),Vector3.one);
            Round(new Rect(-width*.5f,-width*.5f,d.magnitude+width,width),c,width*.5f);GUI.matrix=old;
        }
        public static void Arc(Vector2 centre,float radius,float fraction,Color c,float width=2,float start=-90)
        {
            int count=Mathf.Max(1,Mathf.CeilToInt(Mathf.Clamp01(fraction)*48));
            for(int i=0;i<count;i++){
                float a=(start+360*fraction*i/count)*Mathf.Deg2Rad,b=(start+360*fraction*(i+1)/count)*Mathf.Deg2Rad;
                Line(centre+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius,centre+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*radius,c,width);
            }
        }
        static readonly Dictionary<string,Texture2D> icons=new Dictionary<string,Texture2D>();
        public static void Icon(string name,Rect rect,Color c)
        {
            if(!icons.TryGetValue(name,out var texture)){texture=Resources.Load<Texture2D>("UI/Icons/"+name);icons[name]=texture;}
            if(!texture)return;var old=GUI.color;GUI.color=c;
            GUI.DrawTexture(rect,texture,ScaleMode.ScaleToFit,true);GUI.color=old;
        }
        public static Texture2D MapImage(CourseMapDefinition map)
        {
            string key=!string.IsNullOrEmpty(map.thumbnail)?map.thumbnail:map.style==CourseMapStyle.Factory?"Factory":map.style==CourseMapStyle.Climb?"Climb":map.style==CourseMapStyle.Surf?"Surf":"Jump";
            if(!maps.TryGetValue(key,out var texture)){texture=Resources.Load<Texture2D>("UI/Maps/"+key);maps[key]=texture;}return texture;
        }
        public static void MapPicture(Rect rect,CourseMapDefinition map,float radius=18)
        {
            var image=MapImage(map);if(image)GUI.DrawTexture(rect,image,ScaleMode.ScaleAndCrop,true,0,Color.white,0,radius);
        }
    }
}
