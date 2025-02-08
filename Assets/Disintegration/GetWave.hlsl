StructuredBuffer<int> _Result;

void Wave_float(in float vertexID, out float output)
{
    output = _Result[vertexID];
}
