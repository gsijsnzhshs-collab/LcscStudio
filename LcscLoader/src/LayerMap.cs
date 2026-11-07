using System;
using PCB;

namespace LcscLoader
{
    internal static class LayerMap
    {
        // AD20 TV6 layer constants: mechanical 1-16 are 57-72, keepout is 56.
        public static bool TryMap(string name, out PCB.TV6_Layer layer, out string error)
        {
            error = null;
            switch (name)
            {
                case "TopLayer": layer = PCB.TV6_Layer.eV6_TopLayer; return true;
                case "BottomLayer": layer = PCB.TV6_Layer.eV6_BottomLayer; return true;
                case "TopSilkLayer": layer = PCB.TV6_Layer.eV6_TopOverlay; return true;
                case "BottomSilkLayer": layer = PCB.TV6_Layer.eV6_BottomOverlay; return true;
                case "TopPasteMaskLayer": layer = PCB.TV6_Layer.eV6_TopPaste; return true;
                case "BottomPasteMaskLayer": layer = PCB.TV6_Layer.eV6_BottomPaste; return true;
                case "TopSolderMaskLayer": layer = PCB.TV6_Layer.eV6_TopSolder; return true;
                case "BottomSolderMaskLayer": layer = PCB.TV6_Layer.eV6_BottomSolder; return true;
                case "BoardOutline":
                case "BoardOutLine": layer = PCB.TV6_Layer.eV6_Mechanical1; return true;
                case "Multi-Layer": layer = PCB.TV6_Layer.eV6_MultiLayer; return true;
                case "TopAssembly": layer = PCB.TV6_Layer.eV6_Mechanical7; return true;
                case "BottomAssembly": layer = PCB.TV6_Layer.eV6_Mechanical8; return true;
                case "Mechanical": layer = PCB.TV6_Layer.eV6_Mechanical15; return true;
                case "3DModel": layer = PCB.TV6_Layer.eV6_Mechanical13; return true;
                case "Document": layer = PCB.TV6_Layer.eV6_Mechanical16; return true;
                case "ComponentShapeLayer": layer = PCB.TV6_Layer.eV6_Mechanical9; return true;
                case "LeadShapeLayer": layer = PCB.TV6_Layer.eV6_Mechanical10; return true;
                case "ComponentMarkingLayer":
                case "ComponentPolarityLayer": layer = PCB.TV6_Layer.eV6_Mechanical11; return true;
                default:
                    layer = PCB.TV6_Layer.eV6_NoLayer;
                    error = "未知层 " + name;
                    return false;
            }
        }
    }
}
