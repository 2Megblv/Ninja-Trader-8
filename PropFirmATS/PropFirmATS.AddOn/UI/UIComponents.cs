using System.Xml.Linq;
using NinjaTrader.NinjaScript.AddOnBase;

namespace PropFirmATS.AddOn.UI
{
    public class PropFirmATSWindow : NTWindow
    {
        public override void Restore(XElement element)
        {
            // Restore window bounds etc.
        }

        public override void Save(XElement element)
        {
            // Save window bounds etc.
        }
    }

    public class PropFirmATSTabPage : NTTabPage
    {
        public override void Restore(XElement element)
        {
            // Restore tab content states
        }

        public override void Save(XElement element)
        {
            // Save tab content states
        }
    }

    public class PropFirmATSTabFactory : INTTabFactory
    {
        public NTTabPage Create()
        {
            return new PropFirmATSTabPage();
        }
    }
}
