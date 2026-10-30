using UnityEngine;
using UnityEngine.SceneManagement;

namespace HalliGalli.Movement
{
    // Select the per-map movement profile before any motor or roster initializes.
    [DefaultExecutionOrder(-300)]
    public sealed class CourseMapController:MonoBehaviour
    {
        public CourseSession session;
        public CourseMapCatalog catalog;
        public CourseMapDefinition current;
        public bool Loading{get;private set;}
        static string pendingScene;
        static int pendingPlayers;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearPending(){pendingScene=null;pendingPlayers=0;}
        void Awake()
        {
            if(pendingScene==gameObject.scene.path){session.roster.playerCount=pendingPlayers;ClearPending();}
            if(current&&current.movementProfile)foreach(var p in session.roster.allParticipants)p.GetComponent<SourceMotor>().profile=current.movementProfile;
        }
        public bool CanSelect(CourseMapDefinition map)=>!Loading&&catalog&&catalog.Contains(map)&&map.playable&&map.movementProfile&&!string.IsNullOrEmpty(map.scenePath)&&Application.CanStreamedLevelBeLoaded(map.scenePath);
        public bool TryStart(CourseMapDefinition map,int players)
        {
            if(players<2||players>6||!CanSelect(map))return false;
            if(map==current&&gameObject.scene.path==map.scenePath){session.roster.StartNewGame(players);return true;}
            pendingScene=map.scenePath;pendingPlayers=players;Loading=true;
            try{
                var operation=SceneManager.LoadSceneAsync(map.scenePath,LoadSceneMode.Single);
                if(operation!=null)return true;
            }catch(System.Exception e){Debug.LogException(e);}
            Loading=false;ClearPending();return false;
        }
    }
}
