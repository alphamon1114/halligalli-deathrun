using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using HalliGalli.Cards;
using HalliGalli.Movement;

namespace HalliGalli.EditorTools
{
    public static class MatchIntegrationBuilder
    {
        [MenuItem("HalliGalli/Cards/Connect Match and Validate")]
        public static void InstallAndValidate()
        {
            EditorSceneManager.OpenScene(CourseBuilder.ScenePath);
            InstallInCurrentScene();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Validate();
        }
        public static void InstallInCurrentScene()
        {
            var session=UnityEngine.Object.FindFirstObjectByType<CourseSession>();
            var deck=AssetDatabase.LoadAssetAtPath<ClassicFruitDeck>(HalliGalli.CardEditor.CardAssetBuilder.DeckPath);
            if(!session||!deck)return;
            // Keep the authored course and character objects; replace only the card preview.
            foreach(var old in UnityEngine.Object.FindObjectsByType<CardTablePreview>(FindObjectsInactive.Include))UnityEngine.Object.DestroyImmediate(old.gameObject);
            var game=session.GetComponent<CourseCardGame>();if(!game)game=session.gameObject.AddComponent<CourseCardGame>();
            var display=game.display;
            if(!display){display=new GameObject("Live card table").AddComponent<CardTableDisplay>();}
            int count=session.participants.Length;
            if(session.roster)session.roster.ApplyLayout(session.roster.PlayerCount);
            if(display.drawAnchors==null||display.drawAnchors.Length!=count){
                if(count!=2)throw new InvalidOperationException("Assign one draw and open anchor per participant before installing a larger table.");
                display.drawAnchors=new Transform[count];display.openAnchors=new Transform[count];
                for(int i=0;i<count;i++){
                    display.drawAnchors[i]=Anchor(display.transform,"Seat "+i+" draw",new Vector3(i==0?-1.28f:1.28f,.890f,-4.85f));
                    display.openAnchors[i]=Anchor(display.transform,"Seat "+i+" open",new Vector3(i==0?-.48f:.48f,.891f,-4.5f));
                }
            }
            game.session=session;game.deck=deck;game.display=display;display.deck=deck;session.cardGame=game;
            var tableView=session.Local.GetComponent<CourseTableView>();if(!tableView)tableView=session.Local.gameObject.AddComponent<CourseTableView>();
            tableView.participant=session.Local;tableView.table=display;game.tableView=tableView;
            display.Initialize(new HalliGalliMatch(deck.Deal(count,game.seed)));
            session.Local.GetComponent<CharacterMotionDriver>().worldCamera.transform.rotation=Quaternion.Euler(22,session.Local.seatAnchor.eulerAngles.y,0);
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }
        static Transform Anchor(Transform parent,string name,Vector3 position){var t=new GameObject(name).transform;t.SetParent(parent);t.position=position;return t;}
        public static void RefreshCardPreviews()
        {
            foreach(var map in CourseMapsBuilder.Catalog.maps){
                if(!map||!map.playable)continue;
                var scene=EditorSceneManager.OpenScene(map.scenePath);
                var session=UnityEngine.Object.FindAnyObjectByType<CourseSession>();
                var game=session.cardGame;
                game.display.Initialize(new HalliGalliMatch(game.deck.Deal(session.roster.PlayerCount,game.seed)));
                EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();
        }
        [MenuItem("HalliGalli/Cards/Validate Match")]
        public static void Validate()
        {
            EditorSceneManager.OpenScene(CourseBuilder.ScenePath);
            PlayerSettings.runInBackground=true;
            new GameObject("Match validation").AddComponent<MatchValidationRunner>();
            EditorApplication.isPaused=false;
            EditorApplication.isPlaying=true;
        }
    }
}
