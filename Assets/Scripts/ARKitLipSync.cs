using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(SkinnedMeshRenderer), typeof(AudioSource))]
public class ARKitLipSync : MonoBehaviour
{
    [System.Serializable]
    public class PhonemeMapping
    {
        public string phoneme;
        public string blendShapeName;
        public float frequencyThreshold;
        public float maxWeight = 100f;
    }

    public PhonemeMapping[] phonemeConfigs;
    public float fftResolution = 512;
    public float smoothingSpeed = 3f;
    public float volumeSensitivity = 50f;
    public float minVolume = 0.01f;

    private SkinnedMeshRenderer faceMesh;
    private AudioSource audioSource;
    private Dictionary<string, int> blendShapeIndexMap = new Dictionary<string, int>();
    private float[] currentWeights;
    private float[] spectrumData;

    void Start()
    {
        faceMesh = GetComponent<SkinnedMeshRenderer>();
        audioSource = GetComponent<AudioSource>();
        audioSource.loop = true;
        audioSource.Play();

        spectrumData = new float[(int)fftResolution];

        // 블렌드쉐이프 인덱스 매핑 초기화
        for(int i=0; i<faceMesh.sharedMesh.blendShapeCount; i++)
        {
            string name = faceMesh.sharedMesh.GetBlendShapeName(i);
            blendShapeIndexMap[name] = i;
            Debug.Log($"BlendShape Index: {i} - {name}");
        }

        currentWeights = new float[faceMesh.sharedMesh.blendShapeCount];
    }

    void Update()
    {
        if (!audioSource.isPlaying || audioSource.clip == null)
        {
            ResetBlendShapes();
            return;
        }

        AnalyzeAudio();
        UpdateBlendShapes();
    }

    void AnalyzeAudio()
    {
        audioSource.GetSpectrumData(spectrumData, 0, FFTWindow.BlackmanHarris);
        
        // 볼륨 계산 (선형 방식)
        float rms = CalculateRMS(audioSource);
        float linearVolume = Mathf.Clamp(rms * 100f, 0f, 100f);

        if(linearVolume < minVolume)
        {
            ResetBlendShapeWeights();
            return;
        }

        foreach(var config in phonemeConfigs)
        {
            if (!blendShapeIndexMap.TryGetValue(config.blendShapeName, out int targetIndex))
            {
                Debug.LogError($"BlendShape not found: {config.blendShapeName}");
                continue;
            }

            float freqValue = GetFrequencyRangeValue(config.frequencyThreshold);
            float targetWeight = Mathf.Clamp(
                freqValue * linearVolume * volumeSensitivity, 
                0, 
                config.maxWeight
            );

            currentWeights[targetIndex] = Mathf.Lerp(
                currentWeights[targetIndex], 
                targetWeight,
                Time.deltaTime * smoothingSpeed
            );
        }
    }

    void UpdateBlendShapes()
    {
        for(int i=0; i<currentWeights.Length; i++)
        {
            faceMesh.SetBlendShapeWeight(i, currentWeights[i]);
        }
    }

    float CalculateRMS(AudioSource source)
    {
        float[] samples = new float[1024];
        source.GetOutputData(samples, 0);
        float sum = 0f;
        foreach(float s in samples) sum += s * s;
        float rms = Mathf.Sqrt(sum / samples.Length);
        return Mathf.Max(rms, 0.0001f);
    }

    float GetFrequencyRangeValue(float targetFreq)
    {
        int bin = Mathf.FloorToInt(targetFreq * fftResolution / AudioSettings.outputSampleRate);
        bin = Mathf.Clamp(bin, 0, spectrumData.Length-1);
        return spectrumData[bin] * 1000f;
    }

    void ResetBlendShapeWeights()
    {
        for(int i=0; i<currentWeights.Length; i++)
        {
            currentWeights[i] = 0f;
        }
    }

    void ResetBlendShapes()
    {
        ResetBlendShapeWeights();
        UpdateBlendShapes();
    }

    // 디버그용 스펙트럼 시각화
    void OnDrawGizmos()
    {
        if (spectrumData == null) return;

        for(int i=0; i<spectrumData.Length; i++)
        {
            float height = spectrumData[i] * 1000;
            Gizmos.color = Color.Lerp(Color.blue, Color.red, height/10f);
            Gizmos.DrawCube(new Vector3(i*0.1f, height/2, 0), new Vector3(0.05f, height, 0.05f));
        }
    }
}