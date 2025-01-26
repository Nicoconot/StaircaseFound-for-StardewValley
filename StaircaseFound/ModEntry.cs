using System;
using System.Reflection;
using GenericModConfigMenu;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Audio;
using StardewValley.Buildings;
using StardewValley.Extensions;
using StardewValley.GameData.Objects;
using StardewValley.Locations;
using xTile.Layers;
using xTile.Tiles;

namespace EasyToolbar
{
    /// <summary>The mod entry point.</summary>
    internal sealed class ModEntry : Mod
    {
        /*********
        ** Properties
        *********/
        /// <summary>The mod configuration from the player.</summary>
        private ModConfig Config;

        //------Location information
        GameLocation currentLocation;
        bool doLocationChecks = false;
        private int ticks;
        private bool isInMineLevel = false;

        //------For each mine level
        bool prepareLevelReady = false;
        bool levelReady = false;

        bool staircaseHasJustBeenUsed = false;
        int numStaircasesInventory = 0;

        Layer currentBuildingLayer;
        TileArray currentTileArray;

        int xTilesLength, yTilesLength;
        private List<NPC> monstersInLevel = new();
        private int[,] discoveredLaddersOrShafts = new int[0, 0];

        private int currentStonesInMineLevel = 0;
        private int amountOfMonstersInLevel = 0;

        /*********** Public methods *********/

        /// <summary>The mod entry point, called after the mod is first loaded.</summary>
        /// <param name="helper">Provides simplified APIs for writing mods.</param>
        public override void Entry(IModHelper helper)
        {
            //Setup
            Config = Helper.ReadConfig<ModConfig>();
            SetEvents(helper);
        }

        #region Setting up

        private void SetEvents(IModHelper helper)
        {
            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.Player.Warped += OnPlayerWarped;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.GameLoop.OneSecondUpdateTicked += OnOneSecondUpdateTicked;
            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.Player.InventoryChanged += OnInventoryChanged;
        }

        private void LoadGenericModConfigSettings()
        {
#pragma warning disable CS8602 // Dereference of a possibly null reference.
            if (!IsConfigFileValid()) return;
            // get Generic Mod Config Menu's API (if it's installed)
            var configMenu = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (configMenu is null)
                return;

            // register mod
            configMenu.Register(
                mod: this.ModManifest,
                reset: () => this.Config = new ModConfig(),
                save: () => this.Helper.WriteConfig(this.Config)
            );

            configMenu.AddBoolOption(
                 mod: this.ModManifest,
                 name: () => "Show notification message",
                 tooltip: () => "Shows a notification in the bottom-left corner of the screen when a ladder or shaft is discovered.",
                 getValue: () => Config.PlayNotificationMessage,
                 setValue: value => Config.PlayNotificationMessage = value
             );

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Play notification sound",
                tooltip: () => "Plays a sound whenever a ladder or shaft is discovered.",
                getValue: () => Config.PlayNotificationSound,
                setValue: value => Config.PlayNotificationSound = value
            );

            configMenu.AddBoolOption(
                 mod: this.ModManifest,
                 name: () => "Check for mine ladders on level load",
                 tooltip: () => "Check if there are any existing ladders or shafts as soon as you enter a new mine level.",
                 getValue: () => Config.CheckForLaddersOnLevelLoad,
                 setValue: value => Config.CheckForLaddersOnLevelLoad = value
             );


#pragma warning restore CS8602 // Dereference of a possibly null reference.
        }

        #endregion

        #region Event callbacks
        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            LoadGenericModConfigSettings();
        }

        //Called when player changes map locations in general
        private void OnPlayerWarped(object sender, WarpedEventArgs e)
        {
            //e.NewLocation.OnStoneDestroyed
            //Monitor.Log($"New location: {e.NewLocation.Name}", LogLevel.Debug);
            currentLocation = e.NewLocation;

            doLocationChecks = true;
            staircaseHasJustBeenUsed = false;
        }

        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (doLocationChecks)
            {
                if (ticks < 3)
                {
                    ticks++;
                    return;
                }

                ticks = 0;
                doLocationChecks = false;
                CheckLocation();
            }

            if (prepareLevelReady && !doLocationChecks)
            {
                if (ticks < 5)
                {
                    ticks++;
                    return;
                }

                ticks = 0;
                prepareLevelReady = false;
                levelReady = true;
            }

            if (isInMineLevel)
            {
                if (currentLocation is not MineShaft shaft) return;
                if (currentStonesInMineLevel > shaft.stonesLeftOnThisLevel)
                {
                    //player broke a stone
                    currentStonesInMineLevel = shaft.stonesLeftOnThisLevel;
                    OnStoneMined();
                }
            }
        }

        private void OnOneSecondUpdateTicked(object sender, OneSecondUpdateTickedEventArgs e)
        {
            if (isInMineLevel && levelReady)
            {
                if (currentLocation is not MineShaft) return;

                CheckAmountOfMonstersInLevel();
            }
        }

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (e.Button.IsActionButton())
            {
                var farmer = Game1.player;
                if (!ValidPlayerChecks(farmer)) return;

                if (farmer.CurrentItem != null && farmer.CurrentItem.QualifiedItemId == "(BC)71")
                {
                    staircaseHasJustBeenUsed = true;
                }
            }
        }

        private void OnInventoryChanged(object sender, InventoryChangedEventArgs e)
        {
            if (isInMineLevel)
            CheckStaircasesInInventory();
        }

        #endregion

        #region Actually doing stuff

        private void CheckStaircasesInInventory()
        {
            if (!ValidPlayerChecks(Game1.player)) return;

            int staircases = Game1.player.Items.CountId("(BC)71");

            if(numStaircasesInventory > staircases) staircaseHasJustBeenUsed = true;
        }

        private void OnStoneMined()
        {
            TryFindLaddersOrShafts();
        }
        private void CheckLocation()
        {
            if (currentLocation is MineShaft shaft)
            {
                levelReady = false;
                //Monitor.Log($"Player is in mineshaft.", LogLevel.Debug);
                isInMineLevel = true;
                currentStonesInMineLevel = shaft.stonesLeftOnThisLevel;
                amountOfMonstersInLevel = 0;

                CheckStaircasesInInventory();

                currentBuildingLayer = shaft.map.RequireLayer("Buildings");
                currentTileArray = currentBuildingLayer.Tiles;
                xTilesLength = currentTileArray.Array.GetLength(0);
                yTilesLength = currentTileArray.Array.GetLength(1);

                discoveredLaddersOrShafts = new int[xTilesLength, yTilesLength];

                CheckAmountOfMonstersInLevel(false);
                TryFindLaddersOrShafts(true);
                prepareLevelReady = true;
            }
            else
            {
                isInMineLevel = false;
                levelReady = false;
                currentStonesInMineLevel = 0;
            }
        }

        private void CheckAmountOfMonstersInLevel(bool checkForLadders = true)
        {
            if (currentLocation == null || currentLocation is not MineShaft) return;
            var shaft = currentLocation as MineShaft;
            monstersInLevel.Clear();
            monstersInLevel.AddRange(shaft.characters.ToList().FindAll(x => x.IsMonster));
            int monsterCount = monstersInLevel.Count;

            if (checkForLadders && monsterCount < amountOfMonstersInLevel)
            {
                //a monster has been slayed, check for ladders
                //possible issue: if a monster spawns afterward, as with a monster surge event, this will not work properly for the first monster in that surge

                TryFindLaddersOrShafts();
            }
            amountOfMonstersInLevel = monsterCount;
        }

        private void TryFindLaddersOrShafts(bool initialSearch = false)
        {
            if (currentLocation is not MineShaft shaft) return;
            try
            {
                //Monitor.Log($"Mineshaft tiles length is [{xTilesLength},{yTilesLength}]", LogLevel.Debug);

                int numberOfInitialLadders = 0;
                int numberOfInitialShafts = 0;

                for (int x = 0; x < xTilesLength; x++)
                {
                    for (int y = 0; y < yTilesLength; y++)
                    {
                        if (currentTileArray[x, y] == null) continue;

                        //Monitor.Log($"Tile index at [{x}, {y}] is {tiles[x,y].TileIndex}|{tiles[x,y].TileIndexProperties}. Type {tiles[x,y].GetType}, tilesheet {tiles[x,y].TileSheet}, layer {tiles[x,y].Layer}", LogLevel.Debug);
                        if (currentTileArray[x, y].TileIndex == 173)
                        {
                            if (discoveredLaddersOrShafts[x, y] > 0)
                            {
                                //Already know about this ladder, move on
                                continue;
                            }
                            discoveredLaddersOrShafts[x, y] = 1;
                            
                            if (!initialSearch && !staircaseHasJustBeenUsed)
                            { 
                                if (Config.PlayNotificationSound) Game1.playSound("newArtifact");
                                if (Config.PlayNotificationMessage) Game1.addHUDMessage(HUDMessage.ForCornerTextbox("A ladder has been discovered!"));
                            }
                            else if (staircaseHasJustBeenUsed && !initialSearch) staircaseHasJustBeenUsed = false;  
                            else numberOfInitialLadders++;

                        }
                        if (shaft.mineLevel > 120)
                        {
                            //if in skull cavern
                            if (currentTileArray[x, y].TileIndex == 174)
                            {
                                if (discoveredLaddersOrShafts[x, y] > 0)
                                {
                                    //Already know about this shaft, move on
                                    continue;
                                }
                                discoveredLaddersOrShafts[x, y] = 2;
                                
                                if (!initialSearch)
                                {
                                    if (Config.PlayNotificationSound) Game1.playSound("newArtifact");
                                    if (Config.PlayNotificationMessage) Game1.addHUDMessage(HUDMessage.ForCornerTextbox("A shaft has been discovered!"));
                                } 
                                else numberOfInitialShafts++;
                            }
                        }
                    }
                }

                if (Config.CheckForLaddersOnLevelLoad
                && initialSearch
                && (numberOfInitialLadders > 0 || numberOfInitialShafts > 0))
                {
                    string message = (numberOfInitialLadders > 0 ? $"{numberOfInitialLadders} ladder" : "")
                    + (numberOfInitialLadders > 1 ? "s" : "")
                    + (numberOfInitialLadders > 1 && numberOfInitialShafts > 0 ? " and" : "")
                    + (numberOfInitialShafts > 0 ? $" {numberOfInitialShafts} shaft" : "")
                    + (numberOfInitialShafts > 1 ? "s" : "")
                    + (numberOfInitialLadders + numberOfInitialShafts > 1 ? " have" : " has")
                    + " been found on this level!";

                    if (Config.PlayNotificationMessage) Game1.addHUDMessage(HUDMessage.ForCornerTextbox(message));
                    if (Config.PlayNotificationSound) Game1.playSound("newArtifact");
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"Error when searching for ladder: {ex.Message}|{ex.StackTrace}");
            }
        }
        #endregion

        #region Safety checks

        private bool ValidPlayerChecks(Farmer currentPlayer)
        {
            if (!currentPlayer.IsLocalPlayer) return false;
            if (currentPlayer.IsBusyDoingSomething()) return false;

            return true;
        }
        private bool IsConfigFileValid()
        {
            bool valid = true;
            if (this.Config == null)
            {
                valid = false;
                Monitor.Log($"Warning! The mod config file is not valid.",
                LogLevel.Debug);
            }

            return valid;
        }
        #endregion
    }
}

