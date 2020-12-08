﻿/**
 * DeathRun mod - Cattlesquat "but standing on the shoulders of giants"
 * 
 * Adapted (w/ substantial changes) from libraryaddict's Radiation Challenge mod -- used w/ permission.
 * 
 * General ideas:
 * * Crafting/charging/scanning/filtering is expensive everywhere (very expensive in radiation)
 * * Power consumption in general is very expensive in radiation, but apart from above tools/vehicles function normally outside radiation.
 * * Gaining/Regaining energy goes slowly in general (very slowly in radiation)
 * 
 * I had to reorganize a bunch of stuff in order to make the new features possible/flexible.
 */

using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

// Power details: 
// - Bases use BaseRoot while the Cyclops is a "true" SubRoot. 
// - Bases have a special PowerRelay type (BasePowerRelay)

namespace DeathRun.Patchers
{
    public class PowerPatcher
    {
        /**
         * Gets the transform/location of a power interface.
         */
        private static UnityEngine.Transform GetTransform(IPowerInterface powerInterface)
        {
            if (powerInterface is BatterySource)
            {
                return ((BatterySource)powerInterface).transform;
            }
            else if (powerInterface is PowerSource)
            {
                return ((PowerSource)powerInterface).transform;
            }
            else if (powerInterface is PowerRelay)
            {
                return ((PowerRelay)powerInterface).transform;
            }

            return null;
        }

        /**
         * @return true if Transform is currently in radiation
         */
        private static bool isTransformInRadiation(Transform transform)
        {
            if (transform == null) return false;
            return RadiationUtils.isInAnyRadiation(transform);
        }

        /**
         * @return true if the power interface is currently in radiation
         */
        private static bool isPowerInRadiation(IPowerInterface powerInterface)
        {
            return isTransformInRadiation(GetTransform(powerInterface));
        }

        /**
         * AdjustAddEnergy - when gaining energy from e.g. solar panel, thermal power station, etc.
         * @return adjusted value of energy to Add back into power grid, based on radiation and difficulty settings
         */
        private static float AdjustAddEnergy(float amount, bool radiation)
        {
            if (radiation)
            {
                if (Config.DEATHRUN.Equals(DeathRun.config.powerCosts))
                {
                    amount /= 4;
                }
                else if (Config.HARD.Equals(DeathRun.config.powerCosts))
                {
                    amount /= 3;
                } 
            }
            else if (Config.DEATHRUN.Equals(DeathRun.config.powerCosts))
            {
                amount /= 3;
            }
            else if (Config.HARD.Equals(DeathRun.config.powerCosts))
            {
                amount /= 2;
            }

            return amount;
        }

        /**
         * AdjustAddConsuming - when spending energy to do anything
         * @return adjusted amount of energy to consume, based on radiation, type-of-use, and difficulty settings
         */
        private static float AdjustConsumeEnergy(float amount, bool radiation, bool isBase)
        {
            if (radiation)
            {
                if (Config.DEATHRUN.Equals(DeathRun.config.powerCosts))
                {
                    amount *= 5;
                }
                else if (Config.HARD.Equals(DeathRun.config.powerCosts))
                {
                    amount *= 3;
                }
            }
            else if (isBase ||
                     (DeathRun.chargingSemaphore || DeathRun.craftingSemaphore || DeathRun.scannerSemaphore || DeathRun.filterSemaphore))
            {
                if (Config.DEATHRUN.Equals(DeathRun.config.powerCosts))
                {
                    amount *= 3;
                }
                else if (Config.HARD.Equals(DeathRun.config.powerCosts))
                {
                    amount *= 2;
                }
            }

            return amount;
        }

        /**
         * ConsumeEnergyBase -- adjusts the amount of power spent at a base
         * @return true if there was enough energy to perform the action
         */
        [HarmonyPrefix]
        public static bool ConsumeEnergyBase(ref IPowerInterface powerInterface, ref float amount, bool __result)
        {
            amount = AdjustConsumeEnergy(amount, isPowerInRadiation(powerInterface), powerInterface is BasePowerRelay);

            // In vanilla if you try to use 5 power from your Fabricator but you only have 4 power, then you not only
            // fail but also lose your 4 power. That was already a little bit irritating, but it becomes grotesque and
            // feels unfair when power requirements are e.g. 15. This next block prevents the not-actually-enough power
            // from being lost, merely doesn't produce the item.
            if (DeathRun.craftingSemaphore && (powerInterface.GetPower() < amount))
            {
                ErrorMessage.AddMessage("Not Enough Power"); 
                __result = false;
                return false;
            } 

            return true;
        }

        /**
         * AddEnergyBase -- adjusts the amount of power added to a base
         */
        [HarmonyPrefix]
        public static void AddEnergyBase(ref IPowerInterface powerInterface, ref float amount)
        {
            amount = AdjustAddEnergy(amount, isPowerInRadiation(powerInterface));
        }

        /**
         * ConsumeEnergyTool - adjust the amount of power consumed by a handheld tool
         */
        [HarmonyPrefix]
        public static void ConsumeEnergyTool(ref float amount)
        {
            amount = AdjustConsumeEnergy(amount, isTransformInRadiation(Player.main.transform), false);
        }

        /**
         * AddEnergyTool - adjust the amount of power added to a handheld tool
         */
        [HarmonyPrefix]
        public static void AddEnergyTool(ref float amount)
        {
            amount = AdjustAddEnergy(amount, isTransformInRadiation(Player.main.transform));
        }

        /**
         * ConsumeEnergyVehicle - adjust the amount of power consumed by a vehicle
         */
        [HarmonyPrefix]
        public static void ConsumeEnergyVehicle(Vehicle __instance, ref float amount)
        {            
            amount = AdjustConsumeEnergy(amount, isTransformInRadiation(__instance.transform), false);
        }

        /**
         * AddEnergyVehicle - adjust the amount of power added to a vehicle
         */
        [HarmonyPrefix]
        public static void AddEnergyVehicle(Vehicle __instance, ref float amount)
        {
            amount = AdjustAddEnergy(amount, isTransformInRadiation(__instance.transform));
        }



        [HarmonyPrefix]
        public static bool ConsumeEnergyFabricatorPrefix(PowerRelay powerRelay, ref float amount, ref bool __result)
        {
            DeathRun.craftingSemaphore = true; // Raises our crafting semaphore before consuming energy at a fabricator
            return true;
        }

        [HarmonyPostfix]
        public static void ConsumeEnergyFabricatorPostfix(PowerRelay powerRelay, ref float amount, ref bool __result)
        {
            DeathRun.craftingSemaphore = false; // Lowers our crafting semaphore after consuming energy at a fabricator
        }

        [HarmonyPrefix]
        public static bool ConsumeEnergyFiltrationPrefix()
        {
            DeathRun.filterSemaphore = true  ; // Raises our filter semaphore before consuming energy at a filtration machine
            return true;
        }

        [HarmonyPostfix]
        public static void ConsumeEnergyFiltrationPostfix()
        {
            DeathRun.filterSemaphore = false; // Lowers our filter semaphore after consuming energy at a filtration machine
        }

        [HarmonyPrefix]
        public static bool ConsumeEnergyScanningPrefix()
        {
            DeathRun.scannerSemaphore = true; // Raises our scanner semaphore before consuming energy at a scanning room
            return true;
        }

        [HarmonyPostfix]
        public static void ConsumeEnergyScanningPostfix()
        {
            DeathRun.scannerSemaphore = false; // Lowers our scanner semaphore after consuming energy at a scanning room
        }

        [HarmonyPrefix]
        public static bool ConsumeEnergyChargingPrefix()
        {
            DeathRun.chargingSemaphore = true; // Raises our charging semaphore before consuming energy at a charger
            return true;
        }

        [HarmonyPostfix]
        public static void ConsumeEnergyChargingPostfix()
        {
            DeathRun.chargingSemaphore = false; // Lowers our charging semaphore after consuming energy at a charger
        }
    }
}