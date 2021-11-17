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
        public static Harmony harmony = new Harmony(pluginGuid);
        public static BepInEx.Logging.ManualLogSource modLogger;
        public const string pluginGuid = "ImmersiveSea.mod";
        public const string pluginName = "Immersive Sea";
        public const string pluginVersion = "0.1";

        // Execute the patch method before or after the original method
        public static void PatchMethod(MethodInfo original, MethodInfo patch, bool isPrefix = false){
            if(isPrefix)
                harmony.Patch(original, prefix: new HarmonyMethod(patch));
            else
                harmony.Patch(original, postfix: new HarmonyMethod(patch));
            modLogger.LogInfo($"Successfully patched method {original.Name} ({original.Module})");
        }

        // Set up all patches
        public void Awake(){
            modLogger = Logger;
            Logger.LogInfo("Mod started");
            
            var originalILog = AccessTools.Method(typeof(NavigationProvider), "ProgressPanelInteractableUpdate");
            var patchILog = AccessTools.Method(typeof(ImmersiveSea), "onInteractableLogUpdated");
            var originalLog = AccessTools.Method(typeof(NavigationProvider), "ProgressPanelUpdate");
            var patchLog = AccessTools.Method(typeof(ImmersiveSea), "onLogUpdated");
            PatchMethod(originalILog, patchILog);
            PatchMethod(originalLog, patchLog);
        }

        public static void onInteractableLogUpdated(string content){
            onLogUpdated(content);
        }

        // Temporarily spawn labels above the ship for each progress update, aka log book entry
        public static void onLogUpdated(string progress){
            var character = GameProvider.Instance.CurrentCharacter;
            var boat = NavigationProvider.Instance.Boat.transform.position - NavigationProvider.Instance.CurrentBaseTileUpdateBehaviourScript.transform.position;
            var text = SpliceText(Regex.Replace(Regex.Replace(progress.Replace("</color>", "\n"), @"<[^>]*>", ""), @"\(.*?\)", ""), 25);
            var currentTile = character.TileConfig.Tiles.First(x => x.Name == character.CurrentTile.Name);

            var label = new Sunless.Game.Entities.Geography.TileLabel(){
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
                gameObject2.layer = int.MaxValue;
				var discoverableLabel = gameObject2.AddComponent<DiscoverableLabel>();
				discoverableLabel.Label = label;
				discoverableLabel.Init(label.Subsurface);
                discoverableLabel.gameObject.transform.localScale = new Vector3(0.85f, 0.85f);
                discoverableLabel.Reveal();
                discoverableLabel.StartCoroutine(fadeLabelOut(discoverableLabel));
			}

            modLogger.LogInfo($"({label.Position.X}, {label.Position.Y}) Placed label {text}");
        }

        public static IEnumerator<object> fadeLabelOut(DiscoverableLabel label){
            float timeSpentWaiting = 0f;
            var mesh = label.gameObject.GetComponent<TextMesh>();
            yield return new WaitForSeconds(label.Label.Label.Count(x => x == ' '));
			while (timeSpentWaiting < 30f)
			{
				timeSpentWaiting += Time.deltaTime;
				mesh.color = Color.Lerp(new Color(mesh.color.r, mesh.color.g, mesh.color.b, 0f), new Color(mesh.color.r, mesh.color.g, mesh.color.b, 1f), 1 - timeSpentWaiting / 3f);
				yield return null;
			}
            Destroy(label);
			yield break;
        }

        // Segment the text into chunks of roughly lineLength characters, cut at the end of words
        public static string SpliceText(string inputText, int lineLength) {
            string[] stringSplit = inputText.Split(' ');
            int charCounter = 0;
            string finalString = "";
        
            for(int i=0; i < stringSplit.Length; i++){
                finalString += stringSplit[i] + " ";
                charCounter += stringSplit[i].Length;
        
                if(charCounter > lineLength || stringSplit[i].Contains("\n")){
                    if(!stringSplit[i].Contains("\n"))
                        finalString += "\n";
                    charCounter = 0;
                }
            }
            return finalString;
        }
    }
}
