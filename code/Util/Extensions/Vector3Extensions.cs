namespace ShrimpleCharacterController;

public static class Vector3Extensions
{
	/// <summary>
	/// Move a vector3 towards a goal by a fixed distance
	/// </summary>
	/// <param name="value"></param>
	/// <param name="target"></param>
	/// <param name="travelSpeed"></param>
	/// <returns></returns>
	public static Vector3 MoveTowards( this Vector3 value, Vector3 target, float travelSpeed )
	{
		var difference = target - value;
		var distance = difference.Length;
		var normal = difference.Normal;

		if ( distance <= travelSpeed || distance == 0f )
		{
			return target;
		}

		return value + normal * travelSpeed;
	}

	/// <summary>
	/// Project a vector along a plane (normal) and scale it back to its original length
	/// </summary>
	/// <param name="value"></param>
	/// <param name="normal"></param>
	/// <returns></returns>
	public static Vector3 ProjectAndScale( this Vector3 value, Vector3 normal )
	{
		var length = value.Length;
		value = Vector3.VectorPlaneProject( value, normal ).Normal;
		value *= length;

		return value;
	}
}
