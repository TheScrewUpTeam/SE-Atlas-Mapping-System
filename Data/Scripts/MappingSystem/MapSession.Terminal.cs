using System.Collections.Generic;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;

namespace TSUT.MappingSystem
{
    public partial class MapSession
    {
        private void OnCustomControlGetter(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            if (block is IMyProjector || block is IMyRadioAntenna)
            {
                foreach (var control in controls)
                {
                    if (control.Id.StartsWith("AMS_"))
                        control.UpdateVisual();
                }
            }
        }
    }
}
