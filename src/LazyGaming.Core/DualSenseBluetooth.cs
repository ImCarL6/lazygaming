using System.Buffers.Binary;

namespace LazyGaming;

public static class DualSenseBluetooth
{
    // Native enhanced-report handshake. All effect-valid flags remain zero:
    // no rumble, trigger, LED, audio or microphone-mute settings are changed.
    public static byte[] FeatureRequest() {
        var report=new byte[78];report[0]=0x31;report[2]=0x10;
        uint crc=0xffffffff;
        void Add(byte value) { crc^=value;for(int bit=0;bit<8;bit++)crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0); }
        Add(0xa2);foreach(byte value in report.AsSpan(0,74))Add(value);
        BinaryPrimitives.WriteUInt32LittleEndian(report.AsSpan(74),~crc);
        return report;
    }
}
