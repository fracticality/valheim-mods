﻿using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Hardcore
{
    [BepInPlugin(Hardcore.UMID, Hardcore.ModName, Hardcore.Version)]
    [BepInDependency(ValheimLib.ValheimLib.ModGuid)]
    [BepInDependency(EquipmentAndQuickSlots.EquipmentAndQuickSlots.PluginId, BepInDependency.DependencyFlags.SoftDependency)]
    public class Hardcore : BaseUnityPlugin
    {
        public const string UMID = "fracticality.valheim.hardcore";
        public const string Version = "1.2.5";
        public const string ModName = "Hardcore";
        Harmony _Harmony;
        public static ManualLogSource Log;        

        public static HardcoreData newProfileData;
        public static List<HardcoreData> hardcoreProfiles = new List<HardcoreData>();
        public static bool clearCustomSpawn = true;
        public static HitData lastHitData;

        public static GameObject uiPanel;
        public static GameObject hardcoreLabel;        
               

        private void Awake()
        {

			Log = Logger;            

            _Harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), null);            
            
        }        

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F4))
            {
                
            }
        }

        private void OnDestroy()
        {
            if (_Harmony != null) _Harmony.UnpatchSelf();
            if (uiPanel != null) Destroy(uiPanel);
            if (hardcoreLabel != null) Destroy(hardcoreLabel);
        }               
        
        public static void ResetHardcorePlayer(PlayerProfile playerProfile)
        {

            Traverse.Create(Player.m_localPlayer)
                    .Field<Skills>("m_skills").Value
                    .Clear();            
            
            playerProfile.ClearCustomSpawnPoint();


            // Clear out custom EquipmentSlotInventory and QuickSlotInventory, if applicable
            AppDomain currentDomain = AppDomain.CurrentDomain;
            List<Assembly> assemblies = new List<Assembly>(currentDomain.GetAssemblies());            
            if (assemblies.Find((Assembly assembly) => { return assembly.GetName().Name == "EquipmentAndQuickSlots"; }) != null)
            {                
                Component extendedPlayerData = Player.m_localPlayer.GetComponent("ExtendedPlayerData");                
                if (extendedPlayerData)
                {                    
                    Traverse tExtendedPlayerData = Traverse.Create(extendedPlayerData);
                    (tExtendedPlayerData.Field("EquipmentSlotInventory").GetValue() as Inventory).RemoveAll();
                    (tExtendedPlayerData.Field("QuickSlotInventory").GetValue() as Inventory).RemoveAll();
                }
            }            
            // End clear custom inventories

            // Reset sync data for MapSharingMadeEasy to prevent removal of all shared pins on next sync
            ZNetView nview = Traverse.Create(Player.m_localPlayer).Field<ZNetView>("m_nview").Value;
            if (nview != null)
            {
                string syncData = nview.GetZDO().GetString("playerSyncData", "");
                if (!string.IsNullOrEmpty(syncData))
                {
                    nview.GetZDO().Set("playerSyncData", "");
                }
            }
            // End reset sync data

            Traverse tMinimap = Traverse.Create(Minimap.instance);
            List<Minimap.PinData> pins = tMinimap.Field<List<Minimap.PinData>>("m_pins").Value;
            List<Minimap.PinData> pinsToRemove = new List<Minimap.PinData>();
            foreach (Minimap.PinData pin in pins)
            {
                if (pin.m_save)
                    pinsToRemove.Add(pin);
            }
            foreach (Minimap.PinData pinToRemove in pinsToRemove)
            {
                Minimap.instance.RemovePin(pinToRemove);
            }

            Minimap.instance.Reset();
            Minimap.instance.SaveMapData();
        }

        // TODO: Change savefile to use json rather than arbitrary binary serialization
        public static bool SaveDataToDisk()
        {
            Directory.CreateDirectory(Utils.GetSaveDataPath() + "/mod_data");
            string filename = Utils.GetSaveDataPath() + "/mod_data/hardcore_characters.fch";
            string fileOld = string.Copy(filename) + ".old";
            string fileNew = string.Copy(filename) + ".new";

            FileStream fStream = File.Create(fileNew);
            var bFormatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
            bFormatter.Serialize(fStream, hardcoreProfiles);

            fStream.Flush(true);
            fStream.Close();
            fStream.Dispose();            

            if (File.Exists(filename))
            {
                if (File.Exists(fileOld))
                {
                    File.Delete(fileOld);
                }
                File.Move(filename, fileOld);
            }
            File.Move(fileNew, filename);
            return true;
        }        

        // TODO: Change savefile to use json rather than arbitrary binary deserialization
        public static bool LoadDataFromDisk()
        {                       
            string filename = Utils.GetSaveDataPath() + "/mod_data/hardcore_characters.fch";
            string fileOld = string.Copy(filename) + ".old";            
            FileStream fStream;                  

            Log.LogInfo($"Loading hardcore character list...");
            try
            {
                if (!File.Exists(filename))
                {
                    Log.LogWarning("- Data file missing! Searching for backup...");
                    if (File.Exists(fileOld))
                    {
                        Log.LogInfo("- Backup found! Restoring...");
                        File.Move(fileOld, filename);
                    }
                    else
                    {
                        Log.LogWarning("- Backup missing!");
                        return false;
                    }
                }
                fStream = File.OpenRead(filename);
            }
            catch
            {
                Log.LogError("Failed to open " + filename);
                return false;
            }            

            try
            {
                var bFormatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();                
                hardcoreProfiles = (List<HardcoreData>)bFormatter.Deserialize(fStream);                

                List<PlayerProfile> playerProfiles = PlayerProfile.GetAllPlayerProfiles();
                foreach (HardcoreData profileData in hardcoreProfiles)
                {
                    if (!playerProfiles.Exists((PlayerProfile profile) => { return profile.GetPlayerID() == profileData.profileID; }))
                    {
                        Log.LogInfo($"- Player ID {profileData.profileID} no longer exists! Removing...");
                        hardcoreProfiles.Remove(profileData);
                    }
                }

                Log.LogInfo($"Loaded {hardcoreProfiles.Count} Hardcore Profile(s)");
            }
            catch (Exception e)
            {                
                Log.LogError("Failed to read from " + filename + ": " + e.Message);
                return false;
            }
            finally
            {                
                fStream.Dispose();
            }

            return true;
        }
    }

    [Serializable()]
    public class HardcoreData
    {
        public long profileID = 0L;

        public bool isHardcore = false;
        public bool skipIntro = false;
        public bool disableTutorials = false;
        public bool hasDied = false;

        public HardcoreData() { }
    }
}
