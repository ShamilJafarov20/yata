using System;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Networking.Connectivity;
using Yata.Helpers;

namespace Yata.Services;

/// <summary>The Wi-Fi network the PC is on: name, signal (0–100) and band ("2.4 GHz", "5 GHz", "6 GHz") when known.</summary>
public sealed record WifiStatus(string Ssid, int SignalPercent, string? Band);

/// <summary>
/// Reads the current Wi-Fi connection from the WLAN service (wlanapi.dll). Null when the PC isn't on
/// Wi-Fi (Ethernet, offline, no adapter) or on any error. On Windows 11 24H2 and later wlanapi
/// refuses the query (access denied) unless Location services are on; then the name and a coarse
/// signal (from the 0–5 bars) come from the WinRT connection profile instead, with no band.
/// </summary>
internal static class WifiInfo
{
    private const uint ClientVersion = 2; // Vista and later
    private const int OpcodeCurrentConnection = 7; // wlan_intf_opcode_current_connection
    private const int OpcodeChannelNumber = 8; // wlan_intf_opcode_channel_number
    private const int StateConnected = 1; // wlan_interface_state_connected
    private const int BssTypeInfrastructure = 1; // dot11_BSS_type_infrastructure
    private const uint ErrorSuccess = 0;

    // WLAN_INTERFACE_INFO_LIST: two DWORDs, then WLAN_INTERFACE_INFO { GUID; WCHAR[256]; state }.
    private const int InterfaceListHeader = 8;
    private const int InterfaceInfoSize = 16 + 512 + 4;
    private const int InterfaceStateOffset = 16 + 512;

    // WLAN_CONNECTION_ATTRIBUTES: state, mode, WCHAR[256] profile, then WLAN_ASSOCIATION_ATTRIBUTES.
    private const int ConnSsidOffset = 520; // DOT11_SSID { ULONG length; UCHAR[32] }
    private const int ConnBssidOffset = 560;
    private const int ConnPhyTypeOffset = 568;
    private const int ConnSignalOffset = 576;
    private const int ConnMinSize = 604;

    // WLAN_BSS_LIST: two DWORDs, then WLAN_BSS_ENTRY (360 bytes) items.
    private const int BssListHeader = 8;
    private const int BssEntrySize = 360;
    private const int BssBssidOffset = 40;
    private const int BssFrequencyOffset = 92; // ulChCenterFrequency, in kHz

    // DOT11_PHY_TYPE values whose band can't be told from the channel number alone (6 GHz reuses 1–233).
    private const int PhyHe = 10; // 802.11ax
    private const int PhyEht = 11; // 802.11be

    public static WifiStatus? Current()
    {
        try
        {
            var (status, anyConnected) = FromWlanApi();
            if (status != null)
                return status;
            // wlanapi couldn't tell (refused without the location permission, or no WLAN service):
            // Windows' connection profiles still give the name and signal bars, though not the band.
            return anyConnected == false ? null : FromConnectionProfiles();
        }
        catch (Exception ex)
        {
            Log.Warn("network", "Falha ao ler a conexão Wi-Fi", ex);
            return null;
        }
    }

    /// <summary>The connection, and whether any Wi-Fi interface is connected (null if wlanapi couldn't list them).</summary>
    private static (WifiStatus? Status, bool? AnyConnected) FromWlanApi()
    {
        IntPtr client = IntPtr.Zero;
        IntPtr list = IntPtr.Zero;
        try
        {
            if (WlanOpenHandle(ClientVersion, IntPtr.Zero, out _, out client) != ErrorSuccess)
                return (null, null); // No WLAN service (desktop without Wi-Fi, service stopped).
            if (WlanEnumInterfaces(client, IntPtr.Zero, out list) != ErrorSuccess || list == IntPtr.Zero)
                return (null, null);

            bool anyConnected = false;
            int count = Marshal.ReadInt32(list);
            for (int i = 0; i < count; i++)
            {
                IntPtr info = list + InterfaceListHeader + i * InterfaceInfoSize;
                if (Marshal.ReadInt32(info, InterfaceStateOffset) != StateConnected)
                    continue;
                anyConnected = true;
                var id = Marshal.PtrToStructure<Guid>(info);
                if (Read(client, id) is { } status)
                    return (status, true);
            }
            return (null, anyConnected);
        }
        finally
        {
            if (list != IntPtr.Zero)
                WlanFreeMemory(list);
            if (client != IntPtr.Zero)
                WlanCloseHandle(client, IntPtr.Zero);
        }
    }

    private static WifiStatus? FromConnectionProfiles()
    {
        foreach (var profile in NetworkInformation.GetConnectionProfiles())
        {
            // Saved networks are listed too; only the connected one has any connectivity.
            if (!profile.IsWlanConnectionProfile || profile.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.None)
                continue;
            string? ssid = profile.WlanConnectionProfileDetails?.GetConnectedSsid();
            if (string.IsNullOrEmpty(ssid))
                ssid = profile.ProfileName;
            if (string.IsNullOrEmpty(ssid))
                continue;
            // 0–5 bars: a coarse percentage.
            return new WifiStatus(ssid, Math.Clamp((profile.GetSignalBars() ?? 0) * 20, 0, 100), null);
        }
        return null;
    }

    private static WifiStatus? Read(IntPtr client, Guid id)
    {
        IntPtr conn = IntPtr.Zero;
        try
        {
            if (WlanQueryInterface(client, ref id, OpcodeCurrentConnection, IntPtr.Zero, out uint size, out conn, out _) != ErrorSuccess
                || conn == IntPtr.Zero || size < ConnMinSize)
                return null; // Not connected, or (24H2+) no location permission.

            int ssidLength = Math.Clamp(Marshal.ReadInt32(conn, ConnSsidOffset), 0, 32);
            var ssidBytes = new byte[ssidLength];
            Marshal.Copy(conn + ConnSsidOffset + 4, ssidBytes, 0, ssidLength);
            string ssid = Encoding.UTF8.GetString(ssidBytes);

            int signal = Math.Clamp(Marshal.ReadInt32(conn, ConnSignalOffset), 0, 100);
            int phy = Marshal.ReadInt32(conn, ConnPhyTypeOffset);
            var bssid = new byte[6];
            Marshal.Copy(conn + ConnBssidOffset, bssid, 0, 6);

            string? band = BandFromFrequency(Frequency(client, id, bssid))
                ?? BandFromChannel(Channel(client, id), phy);
            return new WifiStatus(ssid, signal, band);
        }
        finally
        {
            if (conn != IntPtr.Zero)
                WlanFreeMemory(conn);
        }
    }

    /// <summary>Center frequency (kHz) of the access point we're associated with, from the scan list; 0 if unknown.</summary>
    // Every network is listed (with an SSID filter the security flag would have to match too) and
    // ours is found by BSSID.
    private static uint Frequency(IntPtr client, Guid id, byte[] bssid)
    {
        IntPtr list = IntPtr.Zero;
        try
        {
            if (WlanGetNetworkBssList(client, ref id, IntPtr.Zero, BssTypeInfrastructure, false, IntPtr.Zero, out list) != ErrorSuccess
                || list == IntPtr.Zero)
                return 0;

            int count = Marshal.ReadInt32(list, 4);
            var entryBssid = new byte[6];
            for (int i = 0; i < count; i++)
            {
                IntPtr entry = list + BssListHeader + i * BssEntrySize;
                Marshal.Copy(entry + BssBssidOffset, entryBssid, 0, 6);
                if (entryBssid.AsSpan().SequenceEqual(bssid))
                    return (uint)Marshal.ReadInt32(entry, BssFrequencyOffset);
            }
            return 0;
        }
        finally
        {
            if (list != IntPtr.Zero)
                WlanFreeMemory(list);
        }
    }

    private static int Channel(IntPtr client, Guid id)
    {
        IntPtr data = IntPtr.Zero;
        try
        {
            if (WlanQueryInterface(client, ref id, OpcodeChannelNumber, IntPtr.Zero, out uint size, out data, out _) != ErrorSuccess
                || data == IntPtr.Zero || size < 4)
                return 0;
            return Marshal.ReadInt32(data);
        }
        finally
        {
            if (data != IntPtr.Zero)
                WlanFreeMemory(data);
        }
    }

    internal static string? BandFromFrequency(uint kHz) => kHz switch
    {
        >= 2_400_000 and < 2_500_000 => "2.4 GHz",
        >= 4_900_000 and < 5_925_000 => "5 GHz",
        >= 5_925_000 and <= 7_125_000 => "6 GHz",
        >= 57_000_000 and <= 71_000_000 => "60 GHz",
        _ => null,
    };

    /// <summary>
    /// Fallback when the scan list doesn't have our access point. Wi-Fi 6/6E/7 links (ax/be) may be
    /// on 6 GHz, whose channel numbers overlap the other bands, so those stay unknown.
    /// </summary>
    internal static string? BandFromChannel(int channel, int phyType)
    {
        if (phyType is PhyHe or PhyEht)
            return null;
        return channel switch
        {
            >= 1 and <= 14 => "2.4 GHz",
            >= 32 and <= 177 => "5 GHz",
            _ => null,
        };
    }

    [DllImport("wlanapi.dll")]
    private static extern uint WlanOpenHandle(uint dwClientVersion, IntPtr pReserved, out uint pdwNegotiatedVersion, out IntPtr phClientHandle);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanCloseHandle(IntPtr hClientHandle, IntPtr pReserved);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanEnumInterfaces(IntPtr hClientHandle, IntPtr pReserved, out IntPtr ppInterfaceList);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanQueryInterface(IntPtr hClientHandle, ref Guid pInterfaceGuid, int opCode, IntPtr pReserved,
        out uint pdwDataSize, out IntPtr ppData, out int pWlanOpcodeValueType);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanGetNetworkBssList(IntPtr hClientHandle, ref Guid pInterfaceGuid, IntPtr pDot11Ssid,
        int dot11BssType, [MarshalAs(UnmanagedType.Bool)] bool bSecurityEnabled, IntPtr pReserved, out IntPtr ppWlanBssList);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr pMemory);
}
