using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MovingBall : MonoBehaviour
{
	// Start is called before the first frame update
	public float distance;
	float original;
    void Start()
	{
		original = transform.position.x;
    }

    //// Update is called once per frame
    void Update()
	{
		const float slow_factor = 0.3f;
		float change = distance * Mathf.Sin(Time.time * slow_factor) + distance * Mathf.Cos(Time.time * slow_factor);
		transform.Translate(new Vector3(change,0,0));
		//		transform.position.x +=  distance * Mathf.Sin(Time.time) + distance * Mathf.Cos(Time.time);
    }
}
