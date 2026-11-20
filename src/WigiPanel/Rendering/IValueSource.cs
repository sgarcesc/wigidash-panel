namespace WigiPanel.Rendering
{
    /// <summary>Supplies the current (already scaled) value for a named sensor binding.</summary>
    public interface IValueSource
    {
        /// <returns>The value, or null when the sensor is unbound or has not reported yet.</returns>
        double? Get(string sensorKey);
    }
}
