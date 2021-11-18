using System.Reflection;
using UnityEngine;
using BepInEx;
using HarmonyLib;
using Sunless.Game.ApplicationProviders;
using Sunless.Game.Utilities;
using Sunless.Game.Scripts.Physics;
using System.Text.RegularExpressions;
using System.Linq;
using System.Collections.Generic;

namespace ImmersiveSea
{
    [BepInPlugin(pluginGuid, pluginName, pluginVersion)]
    public class ImmersiveSea : BaseUnityPlugin
    {
        public static Harmony patcher = new Harmony(pluginGuid);
        public static BepInEx.Logging.ManualLogSource Log;
        public static List<DiscoverableLabel> Labels = new List<DiscoverableLabel>();
        public const string pluginGuid = "ImmersiveSea.mod";
        public const string pluginName = "Immersive Sea";
        public const string pluginVersion = "0.1";

        // Set up all patches
        public void Awake()
        {
            Log = Logger;
            Logger.LogInfo("Mod started");

            var originalILog = AccessTools.Method(typeof(NavigationProvider), "ProgressPanelInteractableUpdate");
            var patchILog = AccessTools.Method(typeof(ImmersiveSea), "OnInteractableLogUpdated");
            var originalLog = AccessTools.Method(typeof(NavigationProvider), "ProgressPanelUpdate");
            var patchLog = AccessTools.Method(typeof(ImmersiveSea), "OnLogUpdated");
            patcher.Patch(originalILog, postfix: new HarmonyMethod(patchILog));
            patcher.Patch(originalLog, postfix: new HarmonyMethod(patchLog));
        }

        public static void OnInteractableLogUpdated(string content)
        {
            OnLogUpdated(content);
        }

        // Temporarily spawn labels above the ship for each progress update, aka log book entry
        public static void OnLogUpdated(string progress)
        {
            var character = GameProvider.Instance.CurrentCharacter;
            var boat = NavigationProvider.Instance.Boat.transform.position - NavigationProvider.Instance.CurrentBaseTileUpdateBehaviourScript.transform.position;
            var text = SpliceText(Regex.Replace(Regex.Replace(progress.Replace("</color>", "\n"), @"<[^>]*>", ""), @"\(.*?\)", ""), 25);
            var currentTile = character.TileConfig.Tiles.First(x => x.Name == character.CurrentTile.Name);

            var label = new Sunless.Game.Entities.Geography.TileLabel()
            {
                Label = text,
                Position = new Sunless.Game.Entities.Location.TilePosition(boat.x, boat.y),
                ParentTile = currentTile,
                // Don't trigger a discovery by the boat, it breaks the headlight subroutine
                Radius = int.MinValue
            };

            var tile = GameObject.Find("Tile");
            GameObject gameObject = PrefabHelper.Instance.Get("Sea/Label");
            bool flag = gameObject == null;
            if (!flag)
            {
                var labelPosition = NavigationProvider.Instance.Boat.transform.position;
                labelPosition.y += 30;
                GameObject gameObject2 = UnityEngine.Object.Instantiate(gameObject, labelPosition, gameObject.transform.rotation) as GameObject;
                gameObject2.name = "Label: " + label.Label;
                gameObject2.tag = "Label";
                gameObject2.layer = 0;
                var discoverableLabel = gameObject2.AddComponent<DiscoverableLabel>();
                discoverableLabel.Label = label;
                discoverableLabel.Init(label.Subsurface);
                discoverableLabel.gameObject.transform.localScale = new Vector3(0.85f, 0.85f);
                discoverableLabel.Reveal();
                discoverableLabel.StartCoroutine(FadeLabelOut(discoverableLabel));
                PushLabel(discoverableLabel);
                Labels.Add(discoverableLabel);
            }

            Log.LogInfo($"({label.Position.X}, {label.Position.Y}) Placed label {text}");
        }

        // Push labels into a free space, preferable within the current travel vector
        public static void PushLabel(DiscoverableLabel label){
            var collided = false;
            int limit = 5;

            do {
                collided = false;
                limit--;
                foreach(var previousLabel in Labels){
                    var pushVector = (label.gameObject.transform.position - previousLabel.gameObject.transform.position).normalized;
                    if(pushVector.magnitude == 0){
                        pushVector = pushVector.RandomPositionInRadius(1);
                    }

                    Log.LogInfo($"Vector {pushVector.ToString()}");
                    while(CheckLabelsCollide(label, previousLabel)){
                        collided = true;
                        label.gameObject.transform.position += pushVector * 10;
                        Log.LogInfo($"{label.gameObject.transform.position}");
                    }
                }
            } while(collided && limit > 0);
        }

        public static bool CheckLabelsCollide(DiscoverableLabel labelA, DiscoverableLabel labelB){
            bool leftOf = labelA.gameObject.transform.position.x <= labelB.gameObject.transform.position.x;
            bool above = labelA.gameObject.transform.position.y >= labelB.gameObject.transform.position.y;

            var linesA = labelA.Label.Label.Split('\n');
            var heightA = linesA.Length;
            var widthA = linesA.Max(x => x.Length);

            var linesB = labelB.Label.Label.Split('\n');
            var heightB = linesB.Length;
            var widthB = linesB.Max(x => x.Length);
            
            bool collidesX, collidesY = false;
            if(leftOf)
                collidesX = labelA.gameObject.transform.position.x + widthA * 2 > labelB.gameObject.transform.position.x - widthB * 2;
            else
                collidesX = labelB.gameObject.transform.position.x + widthB * 2 > labelA.gameObject.transform.position.x - widthA * 2;
            if(above)
                collidesY = labelA.gameObject.transform.position.y - heightA * 5 < labelB.gameObject.transform.position.y + heightB * 5;
            else
                collidesY = labelB.gameObject.transform.position.y - heightB * 5 < labelA.gameObject.transform.position.y + heightA * 5;

            return collidesX && collidesY;
        }

        public static IEnumerator<object> FadeLabelOut(DiscoverableLabel label)
        {
            float timeSpentWaiting = 0f;
            var mesh = label.gameObject.GetComponent<TextMesh>();
            yield return new WaitForSeconds(label.Label.Label.Count(x => x == ' '));
            while (timeSpentWaiting < 3f)
            {
                timeSpentWaiting += Time.deltaTime;
                mesh.color = Color.Lerp(new Color(mesh.color.r, mesh.color.g, mesh.color.b, 0f), new Color(mesh.color.r, mesh.color.g, mesh.color.b, 1f), 1 - timeSpentWaiting / 3f);
                yield return null;
            }
            Labels.Remove(label);
            Destroy(label);
            yield break;
        }

        // Segment the text into chunks of roughly lineLength characters, cut at the end of words
        public static string SpliceText(string inputText, int lineLength)
        {
            string[] stringSplit = inputText.Split(' ');
            int charCounter = 0;
            string finalString = "";

            for (int i = 0; i < stringSplit.Length; i++)
            {
                finalString += stringSplit[i] + " ";
                charCounter += stringSplit[i].Length;

                if (charCounter > lineLength || stringSplit[i].Contains("\n"))
                {
                    if (!stringSplit[i].Contains("\n"))
                        finalString += "\n";
                    charCounter = 0;
                }
            }
            return finalString;
        }
    }
}