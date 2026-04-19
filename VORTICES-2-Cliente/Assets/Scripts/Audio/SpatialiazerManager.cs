using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Aplica configuracion de espacializacion 3D a AudioSources de objetos.
///
/// Uso rapido:
/// 1) Agrega este componente a un GameObject manager.
/// 2) Selecciona un alcance (Lista, Hijos o Escena Completa).
/// 3) Ajusta Spatial Blend, distancias y Doppler.
/// 4) Ejecuta "Apply Now" en el inspector o activa Apply On Start.
/// </summary>
[DisallowMultipleComponent]
public class SpatialiazerManager : MonoBehaviour
{
	public enum TargetScope
	{
		ExplicitList,
		ChildrenOfThisObject,
		WholeScene
	}

	[Header("Objetivos")]
	[SerializeField] private TargetScope targetScope = TargetScope.ChildrenOfThisObject;
	[SerializeField] private bool includeInactive = true;

	[Tooltip("Solo se usa cuando Target Scope = Explicit List")]
	[SerializeField] private List<AudioSource> targetSources = new List<AudioSource>();

	[Header("Aplicacion")]
	[SerializeField] private bool applyOnStart = true;
	[SerializeField] private bool applyInOnValidate = false;

	[Header("Espacializacion 3D")]
	[SerializeField, Range(0f, 1f)] private float spatialBlend = 1f;
	[SerializeField, Range(0f, 5f)] private float dopplerLevel = 0f;
	[SerializeField] private AudioRolloffMode rolloffMode = AudioRolloffMode.Logarithmic;
	[SerializeField, Min(0f)] private float minDistance = 1f;
	[SerializeField, Min(0.01f)] private float maxDistance = 20f;
	[SerializeField, Range(0f, 360f)] private float spread = 0f;
	[SerializeField] private bool spatialize = false;

	private void Start()
	{
		if (applyOnStart)
		{
			ApplyToTargets();
		}
	}

	[ContextMenu("Apply Now")]
	public void ApplyToTargets()
	{
		List<AudioSource> sources = ResolveTargets();

		for (int i = 0; i < sources.Count; i++)
		{
			AudioSource source = sources[i];
			if (source == null)
			{
				continue;
			}

			ApplySpatialSettings(source);
		}

		Debug.Log($"[SpatialiazerManager] AudioSources actualizados: {sources.Count}", this);
	}

	public void RegisterAndApply(AudioSource source)
	{
		if (source == null)
		{
			return;
		}

		if (!targetSources.Contains(source))
		{
			targetSources.Add(source);
		}

		ApplySpatialSettings(source);
	}

	public void Unregister(AudioSource source)
	{
		targetSources.Remove(source);
	}

	private List<AudioSource> ResolveTargets()
	{
		List<AudioSource> resolved = new List<AudioSource>();

		switch (targetScope)
		{
			case TargetScope.ExplicitList:
				for (int i = 0; i < targetSources.Count; i++)
				{
					AudioSource source = targetSources[i];
					if (source != null && !resolved.Contains(source))
					{
						resolved.Add(source);
					}
				}
				break;

			case TargetScope.ChildrenOfThisObject:
				AudioSource[] childSources = GetComponentsInChildren<AudioSource>(includeInactive);
				for (int i = 0; i < childSources.Length; i++)
				{
					if (!resolved.Contains(childSources[i]))
					{
						resolved.Add(childSources[i]);
					}
				}
				break;

			case TargetScope.WholeScene:
				AudioSource[] sceneSources = FindObjectsOfType<AudioSource>(includeInactive);
				for (int i = 0; i < sceneSources.Length; i++)
				{
					if (!resolved.Contains(sceneSources[i]))
					{
						resolved.Add(sceneSources[i]);
					}
				}
				break;
		}

		return resolved;
	}

	private void ApplySpatialSettings(AudioSource source)
	{
		source.spatialBlend = spatialBlend;
		source.dopplerLevel = dopplerLevel;
		source.rolloffMode = rolloffMode;
		source.minDistance = minDistance;
		source.maxDistance = Mathf.Max(maxDistance, minDistance + 0.01f);
		source.spread = spread;
		source.spatialize = spatialize;
	}

	private void OnValidate()
	{
		if (!applyInOnValidate)
		{
			return;
		}

		ApplyToTargets();
	}
}
