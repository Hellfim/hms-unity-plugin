using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace HmsPlugin
{
    internal class HelpboxAGConnectFile : IDrawer
    {
        private HelpBox.HelpBox _helpBox;

        private bool hasAGConnectFile;

        public HelpboxAGConnectFile()
        {
            _helpBox = new HelpBox.HelpBox($"Please import your {HMSGradlePaths.ConnectServicesFileName} file to StreamingAssets folder", UnityEditor.MessageType.Error);
            hasAGConnectFile = File.Exists(HMSGradlePaths.ConnectServicesFilePath);
        }

        public void Draw()
        {
            if (!hasAGConnectFile)
                _helpBox.Draw();
        }
    }
}
