using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ConVar;
using Rust;
using UnityEngine;

public class PrefabPoolWarmup
{
	public static void Run()
	{
		if (!Rust.Application.isLoadingPrefabs)
		{
			Rust.Application.isLoadingPrefabs = true;
			string[] assetList = GetAssetList();
			for (int i = 0; i < assetList.Length; i++)
			{
				PrefabWarmup(assetList[i]);
			}
			Rust.Application.isLoadingPrefabs = false;
		}
	}

	public static IEnumerator Run(float deltaTime, Action<string> statusFunction = null, string format = null)
	{
		if (UnityEngine.Application.isEditor || Rust.Application.isLoadingPrefabs || !Pool.prewarm)
		{
			yield break;
		}
		Rust.Application.isLoadingPrefabs = true;
		string[] prewarmAssets = GetAssetList();
		Stopwatch sw = Stopwatch.StartNew();
		for (int i = 0; i < prewarmAssets.Length; i++)
		{
			if (sw.Elapsed.TotalSeconds > (double)deltaTime || i == 0 || i == prewarmAssets.Length - 1)
			{
				statusFunction?.Invoke(string.Format((format != null) ? format : "{0}/{1}", i + 1, prewarmAssets.Length));
				yield return CoroutineEx.waitForEndOfFrame;
				sw.Reset();
				sw.Start();
			}
			PrefabWarmup(prewarmAssets[i]);
		}
		Rust.Application.isLoadingPrefabs = false;
	}

	public static string[] GetAssetList()
	{
		return (from x in GameManifest.Current.prefabProperties
			where x.pool
			select x.name).ToArray();
	}

	private static void PrefabWarmup(string path)
	{
		if (string.IsNullOrEmpty(path))
		{
			return;
		}
		GameObject gameObject = GameManager.server.FindPrefab(path);
		if (gameObject != null && gameObject.SupportsPooling())
		{
			int serverCount = gameObject.GetComponent<Poolable>().ServerCount;
			List<GameObject> list = new List<GameObject>();
			for (int i = 0; i < serverCount; i++)
			{
				list.Add(GameManager.server.CreatePrefab(path));
			}
			for (int j = 0; j < serverCount; j++)
			{
				GameManager.server.Retire(list[j]);
			}
		}
	}
}
