using System;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Security.ExchangeActiveSyncProvisioning;
using Windows.System.Profile;

namespace Mizon.Windows.Common
{
    public static class DeviceInformations
    {
        public static Version GetOsVersion()
        {
            return Environment.OSVersion.Version;
        }

        public static string GetDeviceIdentifier()
        {
            var systemIdentificationInfo = SystemIdentification.GetSystemIdForPublisher();

            if (systemIdentificationInfo?.Id == null)
                return string.Empty;

            var bytes = systemIdentificationInfo.Id.ToArray();

            return Convert.ToHexString(bytes);
        }

        public static string GetDeviceModel()
        {
            var easClientDeviceInformation = new EasClientDeviceInformation();
            return easClientDeviceInformation.SystemProductName;
        }

        public static string GetDeviceName()
        {
            var easClientDeviceInformation = new EasClientDeviceInformation();
            return easClientDeviceInformation.FriendlyName;
        }

        public static string GetDeviceUsername()
        {
            return Environment.UserName;
        }
    }
}
