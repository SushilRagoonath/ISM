using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Random = UnityEngine.Random;
public class VPLCreator : MonoBehaviour
{
	public int lightCount;
	public float reflectProb;
	public float destoryTime;
	private int amountOfLights = 0;
	int fixedPathLength;
	// Start is called before the first frame update
	GameObject template;
	GameObject sphere_template;
	GameObject light_parent;
	GameObject[] object_with_mesh;
	public float multiplier;
	List<Vector3> points = new List<Vector3>();// point cloud for imperfect shadow maps
	void process_all_meshes(){
		Camera cam = (Camera) GameObject.Find("Main Camera").GetComponent<Camera>();
		object_with_mesh=(GameObject[]) GameObject.FindGameObjectsWithTag("WantPointGeometry");
		
		print(object_with_mesh.Length);
		float[] areas = new float[object_with_mesh.Length];
		float total_area = 0.0f;
		for( int i =0 ; i< object_with_mesh.Length;i++){
			//Matrix4x4 objectMVP = object_with_mesh[i].transform.localToWorldMatrix* cam.previousViewProjectionMatrix;
			Mesh mesh = object_with_mesh[i].GetComponent<MeshFilter>().sharedMesh;
			int points_to_pick = (int) (mesh.bounds.size.magnitude * multiplier ); // Magic number
			for(int j = 0 ; j < points_to_pick; j++){
				int rand_index = Random.RandomRange(0,mesh.vertexCount );
				Vector3 vert = object_with_mesh[i].transform.TransformPoint(mesh.vertices[rand_index]);
				points.Add(vert);
				//int rand_triangle = (int) Random.RandomRange(0.0f,mesh.triangles.Max()); // Random index
				//rand_triangle = mesh.triangles.ToList().IndexOf(rand_triangle);
				//points.Add(
				//	mesh.vertices[mesh.triangles[rand_triangle] ] ); // Not sure if correct.
			}
		}
		//print("Point count");
		//print(points.Count);
		
	}
	void OnDrawGizmos(){
		for(int i =0; i< points.Count;i++){
			Gizmos.DrawSphere(points[i],0.1f);
			
		}
	}
	void make_light(Vector3 point,Color color)
	{
		//assume template
		
		GameObject new_light = GameObject.Instantiate(template);
		new_light.transform.position = point;
		new_light.GetComponent<Light>().color = color;
		Destroy(new_light,destoryTime + Random.value); // template gets destroyed if seperated into custom script
		new_light.transform.parent = light_parent.transform;
	}
	void Start(){
		double now = Time.realtimeSinceStartupAsDouble;
		process_all_meshes();
		print("Process time");
		sphere_template = GameObject.Find("SphereTemplate");
		make_shadow_mesh();
		print(Time.realtimeSinceStartupAsDouble - now);
		RenderSettings.ambientIntensity = 0.1f;
		RenderSettings.ambientLight = Color.black;
		template =  GameObject.Instantiate(GameObject.Find("LightTemplate")); // template dies do reference becomes null.\
		light_parent =  GameObject.Instantiate(GameObject.Find("LightParent")); // template dies do reference becomes null.\
	}
	void FixedUpdate()
	{
		amountOfLights = light_parent.transform.childCount;
		while(amountOfLights <= lightCount) {
			bool first = true;
			bool hit = false;
			Vector3 random_dir;
			RaycastHit ray = default;
			while(first || Random.Range(0.0f,1.0f)>reflectProb ){
				random_dir = Random.insideUnitSphere.normalized;
				random_dir.y = -Mathf.Abs(random_dir.y);
				hit = Physics.Raycast(transform.position, random_dir,
					out ray,500);
				first = false;
			}
			if (hit) {
				//print("yo");
				MeshRenderer render = ray.collider.GetComponent<MeshRenderer>();
				Texture2D tMap = (Texture2D)render.sharedMaterial.mainTexture;
				//print(render.sharedMaterial.name);
				/*Vector2 pCoord = raycastHit.textureCoord;
				pCoord.x *= tMap.width;
				pCoord.y *= tMap.height;*/
				// Introduce Halton sequence
				int x = Mathf.FloorToInt(ray.point.x);
				int y = Mathf.FloorToInt(ray.point.y);
				Color color;
				//if (render.material.HasTexture("Base Map"))
				//{
					
				color = tMap.GetPixel(x , y);
				//}
				//else
				//{ 
				//	color = render.material.color;
				//}
				make_light(ray.point + ray.normal,color);
				amountOfLights += 1;
			}
		}
	}
	
	void make_shadow_mesh(){
		GameObject empty = new GameObject("SphereParent");
		for(int i=0 ;i< points.Count;i++){
			GameObject clone = Instantiate(sphere_template);
			clone.transform.position = points[i];
			clone.transform.parent = empty.transform;
		}
	}
	// Update is called once per frame
	//void Update()
	//{
	//	 for (int i = 0; i < lightCount; i++) {
	//	     var random_dir = Random.insideUnitSphere.normalized;
	//	     RaycastHit ray;
	//	     var hit =Physics.Raycast(transform.position, random_dir,
	//	         out ray,500);
	//	     if (hit)
	//	     {
	//	         //print("yo");
	//	         make_light(ray.point + ray.normal);
	//	     }
	//	 }
	//}
}
