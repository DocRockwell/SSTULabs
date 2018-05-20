﻿using System;
using UnityEngine;

namespace SSTUTools
{
    public class SSTUFuelSelection : PartModule
    {

        /// <summary>
        /// If >=0, controls what CONTAINER to adjust the fuel type of, to the corresponding value of the currently selected fuel type. 
        /// </summary>
        [KSPField]
        public int containerIndex = -1;

        /// <summary>
        /// Controls which ModuleRCS this module interacts with.  Defaults to -1.  Set to >=0 to enable rcs module interaction.
        /// </summary>
        [KSPField]
        public int rcsModuleIndex = -1;

        /// <summary>
        /// Controls which ModuleEngines this module interacts with.  Defaults to -1.  Set to >=0 to enable rcs module interaction.
        /// </summary>
        [KSPField]
        public int engineModuleIndex = -1;

        [KSPField]
        public string label = "Fuel Type";

        /// <summary>
        /// The currently selected fuel type.  If specified in the config, used as a 'default' fuel type, otherwise initialized to the first fuel type.
        /// </summary>
        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Fuel Type"),
         UI_ChooseOption(suppressEditorShipModified = true)]
        public string currentFuelType = string.Empty;

        /// <summary>
        /// Tracks if the initial fuel type update has been done, updating external modules with the fuel type specified in this module as default.
        /// </summary>
        [KSPField(isPersistant = true)]
        public bool initializedExternal = false;

        [Persistent]
        public string configNodeData = string.Empty;

        private FuelTypeISP[] fuelTypes;
        private FuelTypeISP fuelType;
        private bool initialized = false;

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            if (string.IsNullOrEmpty(configNodeData)) { configNodeData = node.ToString(); }
            init();
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            init();

            Fields[nameof(currentFuelType)].uiControlEditor.onFieldChanged = delegate (BaseField a, System.Object b)
            {
                this.actionWithSymmetry(m =>
                {
                    if (m != this) { m.currentFuelType = currentFuelType; }
                    m.fuelType = Array.Find(m.fuelTypes, s => s.fuelPreset.name == m.currentFuelType);
                    m.updateFuelType();
                });
            };

            Fields[nameof(currentFuelType)].guiName = label;
            Fields[nameof(currentFuelType)].guiActiveEditor = fuelTypes.Length > 1;
            string[] names = SSTUUtils.getNames(fuelTypes, m => m.fuelPreset.name);
            this.updateUIChooseOptionControl(nameof(currentFuelType), names, names, true, currentFuelType);
        }

        public void Start()
        {
            if (!initializedExternal)
            {
                initializedExternal = true;
                updateFuelType();
            }
        }

        private void init()
        {
            if (initialized) { return; }
            initialized = true;
            ConfigNode node = SSTUConfigNodeUtils.parseConfigNode(configNodeData);

            ConfigNode[] fuelTypeNodes = node.GetNodes("FUELTYPE");
            int len = fuelTypeNodes.Length;
            fuelTypes = FuelTypeISP.parse(fuelTypeNodes);
            fuelType = Array.Find(fuelTypes, m => m.fuelPreset.name == currentFuelType);
            if (fuelType == null && (fuelTypes != null && fuelTypes.Length > 0))
            {
                SSTULog.error("ERROR: SSTUModularRCS - currentFuelType was null for value: " + currentFuelType);
                fuelType = fuelTypes[0];
                currentFuelType = fuelType.fuelPreset.name;
                SSTULog.error("Assigned default fuel type of: " + currentFuelType + ".  This is likely a config error that needs to be corrected.");
            }
            else if (fuelTypes == null || fuelTypes.Length < 1)
            {
                SSTULog.error("ERROR: SSTURCSFuelSelection - No fuel type definitions found.");
            }
        }

        private void updateFuelType()
        {
            updateContainerFuelType(fuelType, part, containerIndex);
            updateEngineFuelType(fuelType, part, engineModuleIndex);
            updateRCSFuelType(fuelType, part, rcsModuleIndex);
        }

        public static void updateRCSFuelType(FuelTypeISP fuelType, Part part, int rcsModuleIndex)
        {
            if (rcsModuleIndex < 0) { return; }
            ModuleRCS[] modules = part.GetComponents<ModuleRCS>();
            int len = modules.Length;
            if (rcsModuleIndex < len)
            {
                ModuleRCS rcsModule = modules[rcsModuleIndex];
                rcsModule.propellants.Clear();
                ConfigNode pNode = fuelType.fuelPreset.getPropellantNode(ResourceFlowMode.ALL_VESSEL_BALANCE);
                if (fuelType.ispCurve != null)
                {
                    pNode.AddNode("atmosphereCurve", fuelType.ispCurve.getNode("atmosphereCurve"));
                }
                rcsModule.OnLoad(pNode);
            }
            else
            {
                SSTULog.error("Could not update fuel type - ModuleRCS could not be found for index: " + rcsModuleIndex + "  There are not enough modules present in the part: " + len);
            }
        }

        public static void updateContainerFuelType(FuelTypeISP fuelType, Part part, int containerIndex)
        {
            if (containerIndex < 0) { return; }
            SSTUVolumeContainer vc = part.GetComponent<SSTUVolumeContainer>();
            if (vc == null)
            {
                SSTULog.error("Could not update fuel type - no SSTUVolumeContainer found in part");
                return;
            }
            if (containerIndex < vc.numberOfContainers)
            {
                vc.setFuelPreset(containerIndex, fuelType.fuelPreset, false);
                vc.recalcVolume();
            }
            else
            {
                SSTULog.error("Could not update fuel type - not enough containers in SSTUVolumeContainer for index: "+containerIndex+" only found: "+vc.numberOfContainers);
            }
        }

        public static void updateEngineFuelType(FuelTypeISP fuelType, Part part, int engineModuleIndex)
        {
            if (engineModuleIndex < 0) { return; }
            ModuleEngines[] engines = part.GetComponents<ModuleEngines>();
            int len = engines.Length;
            if (engineModuleIndex < len)
            {
                ModuleEngines engine = engines[engineModuleIndex];
                engine.propellants.Clear();
                ConfigNode pNode = fuelType.fuelPreset.getPropellantNode(ResourceFlowMode.ALL_VESSEL_BALANCE);
                if (fuelType.ispCurve != null)
                {
                    pNode.AddNode("atmCurve", fuelType.ispCurve.getNode("atmCurve"));
                }
                engine.OnLoad(pNode);
            }
            else
            {
                SSTULog.error("Could not update fuel type - ModuleEngines could not be found for index: " + engineModuleIndex + "  There are not enough modules present in the part: " + len);
            }
        }

        public class FuelTypeISP
        {
            public readonly ContainerFuelPreset fuelPreset;
            public readonly FloatCurve ispCurve;
            public FuelTypeISP(ConfigNode node)
            {
                fuelPreset = VolumeContainerLoader.getPreset(node.GetStringValue("name"));
                if (fuelPreset == null)
                {
                    SSTULog.error("Could not locate fuel preset for name: "+node.GetStringValue("name"));
                }
                if (node.HasNode("ispCurve"))
                {
                    ispCurve = node.GetFloatCurve("ispCurve");
                }
                else
                {
                    ispCurve = null;
                }
            }

            public static FuelTypeISP[] parse(ConfigNode[] nodes)
            {
                int len = nodes.Length;
                FuelTypeISP[] isps = new FuelTypeISP[len];
                for (int i = 0; i < len; i++)
                {
                    isps[i] = new FuelTypeISP(nodes[i]);
                }
                return isps;
            }
        }

    }
}
