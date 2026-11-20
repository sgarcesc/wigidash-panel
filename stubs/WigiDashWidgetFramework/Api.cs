// Compile-only declarations of the WigiDash widget SDK (v1) surface that WigiPanel uses.
// See WigiDashWidgetFramework.csproj for why this exists. Rules for editing:
//  - Interfaces the widget IMPLEMENTS (IWidgetBase, IWidgetObject, IWidgetInstance) must list every
//    member of the real interface, or the Manager fails to load the widget with a TypeLoadException.
//  - Interfaces the widget only CALLS (IWidgetManager) need just the members it calls, with the real signatures.
//  - Enum values, type kinds (class/struct) and namespaces must match the real assembly.
// Nothing here has behaviour; every body throws, because this assembly is never loaded at runtime.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Controls;
using WigiDashWidgetFramework.WidgetUtility;

namespace WigiDashWidgetFramework
{
    public interface IWidgetBase
    {
        Guid Guid { get; }
        string Name { get; }
        string Author { get; }
        string Website { get; }
        string Description { get; }
        Version Version { get; }
        SdkVersion TargetSdk { get; }
        List<WidgetSize> SupportedSizes { get; }
        Bitmap PreviewImage { get; }
    }

    public interface IWidgetObject : IWidgetBase
    {
        IWidgetManager WidgetManager { get; set; }
        Bitmap WidgetThumbnail { get; }
        string LastErrorMessage { get; set; }
        Bitmap GetWidgetPreview(WidgetSize widgetSize);
        IWidgetInstance CreateWidgetInstance(WidgetSize widgetSize, Guid instanceGuid);
        bool RemoveWidgetInstance(Guid instanceGuid);
        WidgetError Load(string resourcePath);
        WidgetError Unload();
    }

    public interface IWidgetInstance : IDisposable
    {
        IWidgetObject WidgetObject { get; }
        Guid Guid { get; }
        WidgetSize WidgetSize { get; }
        event WidgetUpdatedEventHandler WidgetUpdated;
        void RequestUpdate();
        void ClickEvent(ClickType click_type, int x, int y);
        UserControl GetSettingsControl();
        void EnterSleep();
        void ExitSleep();
    }

    /// <summary>Subset of the Manager's services: only what WigiPanel calls.</summary>
    public interface IWidgetManager
    {
        bool StoreSetting(IWidgetInstance widgetInstance, string settingId, string settingValue, bool isCloneable = true);
        bool LoadSetting(IWidgetInstance widgetInstance, string settingId, out string settingValue);
        bool AddMonitoringItem(SensorItem sensorItem);
        bool RemoveMonitoringItem(SensorItem sensorItem);
        List<SensorItem> GetSensorList();
        void WriteLogMessage(IWidgetInstance widgetInstance, LogLevel logLevel, string logMessage, string verboseLogMessage = "");
        event SensorUpdatedEventHandler SensorUpdated;
    }

    public delegate void WidgetUpdatedEventHandler(IWidgetInstance widget_instance, WidgetUpdatedEventArgs e);

    public delegate void SensorUpdatedEventHandler(SensorItem item, double value);

    public class WidgetUpdatedEventArgs : EventArgs
    {
        public Point Offset { get; set; }
        public Bitmap WidgetBitmap { get; set; }
        public int WaitMax { get; set; }
    }

    public class SensorItem
    {
        public Guid Guid;
        public int ReadingType;
        public int SensorId1;
        public int SensorId2;
        public string Source;
        public string Name;
        public string Unit;

        public SensorItem(Guid guid, int reading_type, int sensor_id1, int sensor_id2, string source, string name, string unit) =>
            throw new NotSupportedException();
    }

    public enum LogLevel
    {
        TRACE = 0,
        DEBUG = 1,
        INFO = 2,
        WARN = 3,
        ERROR = 4,
        FATAL = 5,
    }
}

namespace WigiDashWidgetFramework.WidgetUtility
{
    public class WidgetSize
    {
        public WidgetSize() => throw new NotSupportedException();
        public WidgetSize(int width, int height) => throw new NotSupportedException();
        public int Width { get; set; }
        public int Height { get; set; }
    }

    public enum WidgetError
    {
        NO_ERROR = 0,
        SDK_VERSION_NOT_SUPPORTED = 1,
        MANAGER_LOAD_FAIL = 2,
        UNDEFINED_ERROR = 3,
        CUSTOM_ERROR = 4,
    }

    public enum ClickType
    {
        None = 0,
        Single = 1,
        Double = 2,
        Long = 3,
        SwipeLeft = 4,
        SwipeRight = 5,
        SwipeUp = 6,
        SwipeDown = 7,
    }

    public enum SdkVersion
    {
        Version_0 = 0,
        Version_1 = 1,
    }
}
