using StardewModdingAPI.Utilities;

class ModConfig
{
   public bool CheckForLaddersOnLevelLoad;
   public bool PlayNotificationMessage;
   public bool PlayNotificationSound;

   public ModConfig()
   {
      CheckForLaddersOnLevelLoad = true;
      PlayNotificationMessage = true;
      PlayNotificationSound = true;
   }
}