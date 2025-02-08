using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

[BurstCompile]
public struct BoolArray
{
    private NativeArray<byte> bools;
    private uint length;

    public BoolArray(uint length, Allocator allocator) : this()
    {
        this.length = length;
        int boolArrayLength = (int)Mathf.Ceil(length / 8f);
        bools = new NativeArray<byte>(boolArrayLength, allocator);
    }

    public bool Get(int index)
    {
        if (index >= length)
        {
            Debug.LogError($"{index} is bigger or equal to the array length {length}");
            return true;
        }
        
        byte boolByte = bools[index / 8];
        byte boolMask = (byte)(1 << (index % 8));
        return (boolByte & boolMask) > 0;
    }

    public void Set(int index, bool value)
    {
        if (index >= length)
        {
            Debug.LogError($"{index} is bigger or equal to the array length {length}");
            return;
        }
        
        byte boolByte = bools[index / 8];
        bools[index / 8] = (byte)((boolByte & ~(1 << index % 8)) | ((value ? (byte)1 : (byte)0) << index % 8));
    }

    public void Dispose()
    {
        bools.Dispose();
    }

    public uint Length()
    {
        return length;
    }
}





