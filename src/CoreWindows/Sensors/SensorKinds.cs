namespace CoreWindows.Sensors;

public enum SensorKind
{
    Temperature, Load, Clock, Power, Voltage, Current, Fan, Control,
    Level, Factor, Data, SmallData, Throughput, Frequency, Flow, Timing, Other
}

public enum ComponentKind
{
    Cpu, Gpu, Memory, MemoryModule, Motherboard, SuperIO, Storage, Network, Other
}