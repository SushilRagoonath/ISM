using UnityEngine;
using UnityEngine.Rendering;
using System.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
public class Shadows { // Extends object to allow setting of copute shader stuff

	const string bufferName = "Shadows";

	const int maxShadowedDirLightCount = 4, maxShadowedOtherLightCount = 768;
	const int maxCascades = 4;

	static string[] directionalFilterKeywords = {
		"_DIRECTIONAL_PCF3",
		"_DIRECTIONAL_PCF5",
		"_DIRECTIONAL_PCF7",
	};

	static string[] otherFilterKeywords = {
		"_OTHER_PCF3",
		"_OTHER_PCF5",
		"_OTHER_PCF7",
	};

	static string[] cascadeBlendKeywords = {
		"_CASCADE_BLEND_SOFT",
		"_CASCADE_BLEND_DITHER"
	};

	static string[] shadowMaskKeywords = {
		"_SHADOW_MASK_ALWAYS",
		"_SHADOW_MASK_DISTANCE"
	};

	static int
		dirShadowAtlasId = Shader.PropertyToID("_DirectionalShadowAtlas"),
		dirShadowMatricesId = Shader.PropertyToID("_DirectionalShadowMatrices"),
		otherShadowAtlasId = Shader.PropertyToID("_OtherShadowAtlas"),
		otherShadowAtlasIdTemp = Shader.PropertyToID("_OtherShadowAtlasTemp"),
		otherShadowMatricesId = Shader.PropertyToID("_OtherShadowMatrices"),
		otherShadowTilesId = Shader.PropertyToID("_OtherShadowTiles"),
		cascadeCountId = Shader.PropertyToID("_CascadeCount"),
		cascadeCullingSpheresId = Shader.PropertyToID("_CascadeCullingSpheres"),
		cascadeDataId = Shader.PropertyToID("_CascadeData"),
		shadowAtlasSizeId = Shader.PropertyToID("_ShadowAtlasSize"),
		shadowDistanceFadeId = Shader.PropertyToID("_ShadowDistanceFade"),
		shadowPancakingId = Shader.PropertyToID("_ShadowPancaking");

	static Vector4[]
		cascadeCullingSpheres = new Vector4[maxCascades],
		cascadeData = new Vector4[maxCascades],
		otherShadowTiles = new Vector4[maxShadowedOtherLightCount];

	static Matrix4x4[]
		dirShadowMatrices = new Matrix4x4[maxShadowedDirLightCount * maxCascades],
		otherShadowMatrices = new Matrix4x4[maxShadowedOtherLightCount];

	struct ShadowedDirectionalLight {
		public int visibleLightIndex;
		public float slopeScaleBias;
		public float nearPlaneOffset;
	}

	ShadowedDirectionalLight[] shadowedDirectionalLights =
		new ShadowedDirectionalLight[maxShadowedDirLightCount];

	struct ShadowedOtherLight {
		public int visibleLightIndex;
		public float slopeScaleBias;
		public float normalBias;
		public bool isPoint;
	}

	ShadowedOtherLight[] shadowedOtherLights =
		new ShadowedOtherLight[maxShadowedOtherLightCount];

	int shadowedDirLightCount, shadowedOtherLightCount;

	CommandBuffer buffer = new CommandBuffer {
		name = bufferName
	};

	ScriptableRenderContext context;

	CullingResults cullingResults;

	ShadowSettings settings;

	bool useShadowMask;

	Vector4 atlasSizes;
	
	
	Mesh sphere_mesh;
	GameObject[] object_with_mesh;
	List<Vector3> points = new List<Vector3>();// point cloud for imperfect shadow maps
	void ProcessSceneMeshesForISM(){
		Camera cam = (Camera) GameObject.Find("Main Camera").GetComponent<Camera>();
		object_with_mesh=(GameObject[]) GameObject.FindGameObjectsWithTag("WantPointGeometry");
		
		float[] areas = new float[object_with_mesh.Length];
		float total_area = 0.0f;
		for( int i =0 ; i< object_with_mesh.Length;i++){
			//Matrix4x4 objectMVP = object_with_mesh[i].transform.localToWorldMatrix* cam.previousViewProjectionMatrix;
			Mesh mesh = object_with_mesh[i].GetComponent<MeshFilter>().sharedMesh;
			int points_to_pick = (int) (mesh.bounds.size.magnitude * 0.4f ); // Magic number
			
			for(int j = 0 ; j < points_to_pick; j++){
				int rand_index = UnityEngine.Random.RandomRange(0,mesh.vertexCount );
				Vector3 vert = object_with_mesh[i].transform.TransformPoint(mesh.vertices[rand_index]);
				points.Add(vert);
				//int rand_triangle = (int) Random.RandomRange(0.0f,mesh.triangles.Max()); // Random index
				//rand_triangle = mesh.triangles.ToList().IndexOf(rand_triangle);
				//points.Add(
				//	mesh.vertices[mesh.triangles[rand_triangle] ] ); // Not sure if correct.
			}
		}
		UnityEngine.Debug.Log("Point count");
		UnityEngine.Debug.Log(points.Count);
		
	}
	// Set trough VPLCreator
	public ComputeShader dilation_shader;
	public ComputeShader erosion_shader;
	RenderTexture renderTexture; // for permanent storage of shadows
	public void Setup (
		ScriptableRenderContext context, CullingResults cullingResults,
		ShadowSettings settings
	) {
		this.context = context;
		this.cullingResults = cullingResults;
		this.settings = settings;
		shadowedDirLightCount = shadowedOtherLightCount = 0;
		useShadowMask = false;
		
		
		//Debug.Log(dilation_shader.name);
		//ProcessSceneMeshesForISM();
		//sphere_mesh = GameObject.CreatePrimitive(PrimitiveType.Sphere).GetComponent<MeshFilter>().sharedMesh;
		//if(!renderTexture) {
		//	renderTexture = new RenderTexture(8192,8192,32, RenderTextureFormat.Default);
		//	renderTexture.enableRandomWrite = true;
		//	renderTexture.Create();
		//}
		dilation_shader = settings.dilationShader;
		
		
	}

	public void Cleanup () {
		buffer.ReleaseTemporaryRT(dirShadowAtlasId);
		if (shadowedOtherLightCount > 0) {
			buffer.ReleaseTemporaryRT(otherShadowAtlasId);
		}
		ExecuteBuffer();
	}

	public Vector4 ReserveDirectionalShadows (
		Light light, int visibleLightIndex
	) {
		if (
			shadowedDirLightCount < maxShadowedDirLightCount &&
			light.shadows != LightShadows.None && light.shadowStrength > 0f
		) {
			float maskChannel = -1;
			LightBakingOutput lightBaking = light.bakingOutput;
			if (
				lightBaking.lightmapBakeType == LightmapBakeType.Mixed &&
				lightBaking.mixedLightingMode == MixedLightingMode.Shadowmask
			) {
				useShadowMask = true;
				maskChannel = lightBaking.occlusionMaskChannel;
			}

			if (!cullingResults.GetShadowCasterBounds(
				visibleLightIndex, out Bounds b
			)) {
				return new Vector4(-light.shadowStrength, 0f, 0f, maskChannel);
			}

			shadowedDirectionalLights[shadowedDirLightCount] =
				new ShadowedDirectionalLight {
					visibleLightIndex = visibleLightIndex,
					slopeScaleBias = light.shadowBias,
					nearPlaneOffset = light.shadowNearPlane
				};
			return new Vector4(
				light.shadowStrength,
				settings.directional.cascadeCount * shadowedDirLightCount++,
				light.shadowNormalBias, maskChannel
			);
		}
		return new Vector4(0f, 0f, 0f, -1f);
	}

	public Vector4 ReserveOtherShadows (Light light, int visibleLightIndex) {
		if (light.shadows == LightShadows.None || light.shadowStrength <= 0f) {
			return new Vector4(0f, 0f, 0f, -1f);
		}

		float maskChannel = -1f;
		LightBakingOutput lightBaking = light.bakingOutput;
		if (
			lightBaking.lightmapBakeType == LightmapBakeType.Mixed &&
			lightBaking.mixedLightingMode == MixedLightingMode.Shadowmask
		) {
			useShadowMask = true;
			maskChannel = lightBaking.occlusionMaskChannel;
		}

		bool isPoint = light.type == LightType.Point;
		int newLightCount = shadowedOtherLightCount + (isPoint ? 6 : 1);
		if (
			newLightCount > maxShadowedOtherLightCount ||
			!cullingResults.GetShadowCasterBounds(visibleLightIndex, out Bounds b)
		) {
			return new Vector4(-light.shadowStrength, 0f, 0f, maskChannel);
		}

		shadowedOtherLights[shadowedOtherLightCount] = new ShadowedOtherLight {
			visibleLightIndex = visibleLightIndex,
			slopeScaleBias = light.shadowBias,
			normalBias = light.shadowNormalBias,
			isPoint = isPoint
		};
		//NOTE Sushil :: hacked strength
		//Vector4 data = new Vector4(
		//	2.5f, shadowedOtherLightCount,
		//	isPoint ? 1f : 0f, maskChannel
		//);

		Vector4 data = new Vector4(
			light.shadowStrength, shadowedOtherLightCount,
			isPoint ? 1f : 0f, maskChannel
		);
		shadowedOtherLightCount = newLightCount;
		return data;
	}

	public void Render () {
		if (shadowedDirLightCount > 0) {
			RenderDirectionalShadows();
		}
		else {
			buffer.GetTemporaryRT(
				dirShadowAtlasId, 1, 1,
				32, FilterMode.Bilinear, RenderTextureFormat.Shadowmap
			);
		}
		if (shadowedOtherLightCount > 0) {
			RenderOtherShadows();
		}
		else {
			buffer.SetGlobalTexture(otherShadowAtlasId, dirShadowAtlasId);
		}

		buffer.BeginSample(bufferName);
		SetKeywords(shadowMaskKeywords, useShadowMask ?
			QualitySettings.shadowmaskMode == ShadowmaskMode.Shadowmask ? 0 : 1 :
			-1
		);
		buffer.SetGlobalInt(
			cascadeCountId,
			shadowedDirLightCount > 0 ? settings.directional.cascadeCount : 0
		);
		float f = 1f - settings.directional.cascadeFade;
		buffer.SetGlobalVector(
			shadowDistanceFadeId, new Vector4(
				1f / settings.maxDistance, 1f / settings.distanceFade,
				1f / (1f - f * f)
			)
		);
		buffer.SetGlobalVector(shadowAtlasSizeId, atlasSizes);
		buffer.EndSample(bufferName);
		ExecuteBuffer();
	}

	void RenderDirectionalShadows () {
		int atlasSize = (int)settings.directional.atlasSize;
		atlasSizes.x = atlasSize;
		atlasSizes.y = 1f / atlasSize;
		buffer.GetTemporaryRT(
			dirShadowAtlasId, atlasSize, atlasSize,
			32, FilterMode.Bilinear, RenderTextureFormat.Shadowmap
		);
		
		buffer.SetRenderTarget(
			dirShadowAtlasId,
			RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store
		);
		buffer.ClearRenderTarget(true, false, Color.clear);
		buffer.SetGlobalFloat(shadowPancakingId, 1f);
		buffer.BeginSample(bufferName);
		ExecuteBuffer();

		int tiles = shadowedDirLightCount * settings.directional.cascadeCount;
		int split = tiles <= 1 ? 1 : tiles <= 4 ? 2 : 4;
		int tileSize = atlasSize / split;

		for (int i = 0; i < shadowedDirLightCount; i++) {
			RenderDirectionalShadows(i, split, tileSize);
		}

		buffer.SetGlobalVectorArray(
			cascadeCullingSpheresId, cascadeCullingSpheres
		);
		buffer.SetGlobalVectorArray(cascadeDataId, cascadeData);
		buffer.SetGlobalMatrixArray(dirShadowMatricesId, dirShadowMatrices);
		SetKeywords(
			directionalFilterKeywords, (int)settings.directional.filter - 1
		);
		SetKeywords(
			cascadeBlendKeywords, (int)settings.directional.cascadeBlend - 1
		);
		buffer.EndSample(bufferName);
		ExecuteBuffer();
	}

	void RenderDirectionalShadows (int index, int split, int tileSize) {
		ShadowedDirectionalLight light = shadowedDirectionalLights[index];
		var shadowSettings =
			new ShadowDrawingSettings(cullingResults, light.visibleLightIndex);
		int cascadeCount = settings.directional.cascadeCount;
		int tileOffset = index * cascadeCount;
		Vector3 ratios = settings.directional.CascadeRatios;
		float cullingFactor =
			Mathf.Max(0f, 0.8f - settings.directional.cascadeFade);
		float tileScale = 1f / split;
		for (int i = 0; i < cascadeCount; i++) {
			cullingResults.ComputeDirectionalShadowMatricesAndCullingPrimitives(
				light.visibleLightIndex, i, cascadeCount, ratios, tileSize,
				light.nearPlaneOffset, out Matrix4x4 viewMatrix,
				out Matrix4x4 projectionMatrix, out ShadowSplitData splitData
			);
			splitData.shadowCascadeBlendCullingFactor = cullingFactor;
			shadowSettings.splitData = splitData;
			if (index == 0) {
				SetCascadeData(i, splitData.cullingSphere, tileSize);
			}
			int tileIndex = tileOffset + i;
			dirShadowMatrices[tileIndex] = ConvertToAtlasMatrix(
				projectionMatrix * viewMatrix,
				SetTileViewport(tileIndex, split, tileSize), tileScale
			);
			buffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
			buffer.SetGlobalDepthBias(0f, light.slopeScaleBias);
			ExecuteBuffer();
			context.DrawShadows(ref shadowSettings);
			buffer.SetGlobalDepthBias(0f, 0f);
		}
	}

	void SetCascadeData (int index, Vector4 cullingSphere, float tileSize) {
		float texelSize = 2f * cullingSphere.w / tileSize;
		float filterSize = texelSize * ((float)settings.directional.filter + 1f);
		cullingSphere.w -= filterSize;
		cullingSphere.w *= cullingSphere.w;
		cascadeCullingSpheres[index] = cullingSphere;
		cascadeData[index] = new Vector4(
			1f / cullingSphere.w,
			filterSize * 1.4142136f
		);
	}

	void RenderOtherShadows () {
		int t = (int) (Time.time);
		//if( t % 1 == 0) return;
		int atlasSize = (int)settings.other.atlasSize;
		atlasSizes.z = atlasSize;
		atlasSizes.w = 1f / atlasSize;
		buffer.GetTemporaryRT(
			otherShadowAtlasId, atlasSize, atlasSize,
			32, FilterMode.Bilinear,RenderTextureFormat.Shadowmap,  //  RenderTextureFormat.Shadowmap,
			RenderTextureReadWrite.Default,1,true
		);
		buffer.GetTemporaryRT(
			otherShadowAtlasIdTemp, atlasSize, atlasSize,
			32, FilterMode.Bilinear,RenderTextureFormat.RFloat,  //  RenderTextureFormat.Shadowmap,
			RenderTextureReadWrite.Default,1,true
		);
		buffer.SetRenderTarget(
			otherShadowAtlasId,
			RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store
		);
		//buffer.SetRenderTarget(renderTexture,
		//		RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store
		//	);
		buffer.ClearRenderTarget(true, false, Color.clear);
		buffer.SetGlobalFloat(shadowPancakingId, 0f);
		buffer.BeginSample(bufferName);
		ExecuteBuffer();

		int tiles = shadowedOtherLightCount;
		int split = tiles <= 1 ? 1 : tiles <= 4 ? 2 : 4;
		int tileSize = atlasSize / split;
		
		for (int i = 0; i < shadowedOtherLightCount;) {
			if (shadowedOtherLights[i].isPoint && t%1==0) {
				RenderPointShadows(i, split, tileSize);
				i += 6;
			}
			else {
				RenderSpotShadows(i, split, tileSize);
				i += 1;
			}
		}

		buffer.SetGlobalMatrixArray(otherShadowMatricesId, otherShadowMatrices);
		buffer.SetGlobalVectorArray(otherShadowTilesId, otherShadowTiles);
		SetKeywords(
			otherFilterKeywords, (int)settings.other.filter - 1
		);
		buffer.EndSample(bufferName);
		ExecuteBuffer();
		//GUI.DrawTexture(new Rect(0,0,100,100) ,otherShadowAtlasId);
	}

	void RenderSpotShadows (int index, int split, int tileSize) {
		ShadowedOtherLight light = shadowedOtherLights[index];
		var shadowSettings =
			new ShadowDrawingSettings(cullingResults, light.visibleLightIndex);
		cullingResults.ComputeSpotShadowMatricesAndCullingPrimitives(
			light.visibleLightIndex, out Matrix4x4 viewMatrix,
			out Matrix4x4 projectionMatrix, out ShadowSplitData splitData
		);
		shadowSettings.splitData = splitData;
		float texelSize = 2f / (tileSize * projectionMatrix.m00);
		float filterSize = texelSize * ((float)settings.other.filter + 1f);
		float bias = light.normalBias * filterSize * 1.4142136f;
		Vector2 offset = SetTileViewport(index, split, tileSize);
		float tileScale = 1f / split;
		SetOtherTileData(index, offset, tileScale, bias);
		otherShadowMatrices[index] = ConvertToAtlasMatrix(
			projectionMatrix * viewMatrix, offset, tileScale
		);
		buffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
		buffer.SetGlobalDepthBias(0f, light.slopeScaleBias);
		ExecuteBuffer();
		context.DrawShadows(ref shadowSettings);
		buffer.SetGlobalDepthBias(0f, 0f);
	}

	void RenderPointShadows (int index, int split, int tileSize)
	{
		// total index should be 8192
		// NOTE Sushil fixed size.
		tileSize = 128;
		split = 4096 / tileSize;
		ShadowedOtherLight light = shadowedOtherLights[index];
		var shadowSettings =
			new ShadowDrawingSettings(cullingResults, light.visibleLightIndex);
		float texelSize = 2f / tileSize;
		float filterSize = texelSize * ((float)settings.other.filter + 1f);
		float bias = light.normalBias * filterSize * 1.4142136f;
		float tileScale = 1f / split;
		float fovBias =
			Mathf.Atan(1f + bias + filterSize) * Mathf.Rad2Deg * 2f - 90f;
		//buffer.DrawMeshInstanced()
		Matrix4x4[] modelMats = new Matrix4x4[points.Count];
		for( int i=0 ; i< points.Count; i++){
			//modelMats[i] = Matrix4x4.identity;
			modelMats[i] = Matrix4x4.Scale(new Vector3(1.0f,1.0f,1.0f)) * Matrix4x4.Translate(points[i]);
		}
		for (int i = 0; i < 6; i++) {
			cullingResults.ComputePointShadowMatricesAndCullingPrimitives(
				light.visibleLightIndex, (CubemapFace)i, fovBias,
				out Matrix4x4 viewMatrix, out Matrix4x4 projectionMatrix,
				out ShadowSplitData splitData
			);
			//viewMatrix.m11 = viewMatrix.m11;
			//viewMatrix.m12 = viewMatrix.m12;
			//viewMatrix.m13 = viewMatrix.m13;
			//viewMatrix.m13 = 0.5f - 0.5f * (viewMatrix.m11*viewMatrix.m11 +viewMatrix.m12*viewMatrix.m12);
			viewMatrix.m11 = -viewMatrix.m11;
			viewMatrix.m12 = -viewMatrix.m12;
			viewMatrix.m13 = -viewMatrix.m13;

			shadowSettings.splitData = splitData;
			int tileIndex = index + i;
			Vector2 offset = SetTileViewport(tileIndex, split, tileSize);
			SetOtherTileData(tileIndex, offset, tileScale, bias);
			otherShadowMatrices[tileIndex] = ConvertToAtlasMatrix(
				projectionMatrix * viewMatrix, offset, tileScale
			);
			Matrix4x4 cProj = Matrix4x4.identity;
			buffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
			buffer.SetGlobalDepthBias(0f, light.slopeScaleBias);
		
			ExecuteBuffer();
			
			context.DrawShadows(ref shadowSettings);
			buffer.SetGlobalDepthBias(0f, 0f);
		}
		//Debug.Log(dilation_shader.FindKernel("CSMain")); // kernel is 0
		//Debug.Log(renderTexture.format);
		//Debug.Log(dilation_shader.name	);
		//dilation_shader.SetTexture(0,"Result",renderTexture,0);
		int threadGroupsX = Mathf.CeilToInt(4096 / 8.0f);
		int threadGroupsY = Mathf.CeilToInt(4096/ 8.0f);
		//buffer.Blit()
		ExecuteBuffer();
		buffer.Blit(otherShadowAtlasId,otherShadowAtlasIdTemp);
		//dilation_shader.SetTexture(0,"Result",renderTexture,0);
		//dilation_shader.Dispatch(0,threadGroupsX,threadGroupsY,1);
		buffer.SetComputeTextureParam(dilation_shader,0,"Result",otherShadowAtlasIdTemp);
		buffer.DispatchCompute(dilation_shader,0,threadGroupsX,threadGroupsY,1);
		// Wrong depth?
		buffer.Blit(otherShadowAtlasIdTemp,otherShadowAtlasId);
		ExecuteBuffer();
	}

	void SetOtherTileData (int index, Vector2 offset, float scale, float bias) {
		float border = atlasSizes.w * 0.5f;
		Vector4 data;
		data.x = offset.x * scale + border;
		data.y = offset.y * scale + border;
		data.z = scale - border - border;
		data.w = bias;
		otherShadowTiles[index] = data;
	}

	Matrix4x4 ConvertToAtlasMatrix (Matrix4x4 m, Vector2 offset, float scale) {
		if (SystemInfo.usesReversedZBuffer) {
			m.m20 = -m.m20;
			m.m21 = -m.m21;
			m.m22 = -m.m22;
			m.m23 = -m.m23;
		}
		m.m00 = (0.5f * (m.m00 + m.m30) + offset.x * m.m30) * scale;
		m.m01 = (0.5f * (m.m01 + m.m31) + offset.x * m.m31) * scale;
		m.m02 = (0.5f * (m.m02 + m.m32) + offset.x * m.m32) * scale;
		m.m03 = (0.5f * (m.m03 + m.m33) + offset.x * m.m33) * scale;
		m.m10 = (0.5f * (m.m10 + m.m30) + offset.y * m.m30) * scale;
		m.m11 = (0.5f * (m.m11 + m.m31) + offset.y * m.m31) * scale;
		m.m12 = (0.5f * (m.m12 + m.m32) + offset.y * m.m32) * scale;
		m.m13 = (0.5f * (m.m13 + m.m33) + offset.y * m.m33) * scale;
		m.m20 = 0.5f * (m.m20 + m.m30);
		m.m21 = 0.5f * (m.m21 + m.m31);
		m.m22 = 0.5f * (m.m22 + m.m32);
		m.m23 = 0.5f * (m.m23 + m.m33);
		return m;
	}

	Vector2 SetTileViewport (int index, int split, float tileSize) {
		Vector2 offset = new Vector2(index % split, index / split);
		buffer.SetViewport(new Rect(
			offset.x * tileSize, offset.y * tileSize, tileSize, tileSize
		));
		return offset;
	}

	void SetKeywords (string[] keywords, int enabledIndex) {
		for (int i = 0; i < keywords.Length; i++) {
			if (i == enabledIndex) {
				buffer.EnableShaderKeyword(keywords[i]);
			}
			else {
				buffer.DisableShaderKeyword(keywords[i]);
			}
		}
	}

	void ExecuteBuffer () {
		context.ExecuteCommandBuffer(buffer);
		buffer.Clear();
	}
}