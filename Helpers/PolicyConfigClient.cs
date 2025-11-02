using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace Mideej.Helpers;

/// <summary>
/// COM interface for changing default audio devices (Windows 10/11 internal API)
/// </summary>
[Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    [PreserveSig]
    int GetMixFormat(string pszDeviceName, IntPtr ppFormat);

    [PreserveSig]
    int GetDeviceFormat(string pszDeviceName, bool bDefault, IntPtr ppFormat);

    [PreserveSig]
    int ResetDeviceFormat(string pszDeviceName);

    [PreserveSig]
    int SetDeviceFormat(string pszDeviceName, IntPtr pEndpointFormat, IntPtr MixFormat);

    [PreserveSig]
    int GetProcessingPeriod(string pszDeviceName, bool bDefault, IntPtr pmftDefaultPeriod, IntPtr pmftMinimumPeriod);

    [PreserveSig]
    int SetProcessingPeriod(string pszDeviceName, IntPtr pmftPeriod);

    [PreserveSig]
    int GetShareMode(string pszDeviceName, IntPtr pMode);

    [PreserveSig]
    int SetShareMode(string pszDeviceName, IntPtr mode);

    [PreserveSig]
    int GetPropertyValue(string pszDeviceName, bool bFxStore, IntPtr key, IntPtr pv);

    [PreserveSig]
    int SetPropertyValue(string pszDeviceName, bool bFxStore, IntPtr key, IntPtr pv);

    [PreserveSig]
    int SetDefaultEndpoint(string pszDeviceName, Role role);

    [PreserveSig]
    int SetEndpointVisibility(string pszDeviceName, bool bVisible);
}

/// <summary>
/// Implementation class for changing default audio devices (Windows 10/11)
/// </summary>
[ComImport]
[Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
internal class CPolicyConfigClient
{
}

/// <summary>
/// Client wrapper for PolicyConfig COM interface
/// </summary>
internal class PolicyConfigClient
{
    private readonly IPolicyConfig? _policyConfig;

    public PolicyConfigClient()
    {
        try
        {
            var policyConfigType = Type.GetTypeFromCLSID(new Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9"));
            if (policyConfigType != null)
            {
                _policyConfig = (IPolicyConfig?)Activator.CreateInstance(policyConfigType);
            }
        }
        catch
        {
            _policyConfig = null;
        }
    }

    /// <summary>
    /// Sets the default audio endpoint for the specified role
    /// </summary>
    public int SetDefaultEndpoint(string deviceId, Role role)
    {
        if (_policyConfig == null)
        {
            return -1; // Failed to create PolicyConfig
        }

        return _policyConfig.SetDefaultEndpoint(deviceId, role);
    }
}
