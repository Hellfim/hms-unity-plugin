using System;
using UnityEngine;

namespace HmsPlugin
{
    public static class HMSGradlePaths
    {
        private const String PluginsPath = "Huawei/Plugins";
        
        public static readonly String BuildPluginsRelativePath = $"Assets/{PluginsPath}";
        public static readonly String AndroidPluginsInternalPath = $"{PluginsPath}/Android";

        public static readonly String MainTemplateGradle = $"{Application.dataPath}/{AndroidPluginsInternalPath}/hmsMainTemplate.gradle";
        public static readonly String LauncherTemplateGradle = $"{Application.dataPath}/{AndroidPluginsInternalPath}/hmsLauncherTemplate.gradle";
        public static readonly String BaseProjectTemplateGradle = $"{Application.dataPath}/{AndroidPluginsInternalPath}/hmsBaseProjectTemplate.gradle";

        public const String ConnectServicesFileName = "agconnect-services.json";
        public static readonly String ConnectServicesFilePath = $"{Application.streamingAssetsPath}/{ConnectServicesFileName}";
    }
}