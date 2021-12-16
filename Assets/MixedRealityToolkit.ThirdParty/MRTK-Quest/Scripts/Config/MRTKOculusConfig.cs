﻿//------------------------------------------------------------------------------ -
//MRTK - Quest
//https ://github.com/provencher/MRTK-Quest
//------------------------------------------------------------------------------ -
//
//MIT License
//
//Copyright(c) 2020 Eric Provencher
//
//Permission is hereby granted, free of charge, to any person obtaining a copy
//of this software and associated documentation files(the "Software"), to deal
//in the Software without restriction, including without limitation the rights
//to use, copy, modify, merge, publish, distribute, sublicense, and / or sell
//copies of the Software, and to permit persons to whom the Software is
//furnished to do so, subject to the following conditions :
//
//The above copyright notice and this permission notice shall be included in all
//copies or substantial portions of the Software.
//
//THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.IN NO EVENT SHALL THE
//AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
//SOFTWARE.
//------------------------------------------------------------------------------ -

using UnityEngine;

namespace prvncher.MixedReality.Toolkit.Config
{
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoad]
#endif
    [CreateAssetMenu(menuName = "MRTK-Quest/MRTK-OculusConfig")]
    public class MRTKOculusConfig : ScriptableObject
    {
        private static MRTKOculusConfig instance;
        public static MRTKOculusConfig Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = Resources.Load<MRTKOculusConfig>("MRTK-OculusConfig");

                    if (instance == null)
                    {
                        UnityEngine.Debug.LogError("Failure to detect MRTK-OculusConfig. Please create an instance using the asset context menu, and place it in any Resources folder.");
                    }
                }
                return instance;
            }
        }
        [Header("Config")]
        [SerializeField]
        [Tooltip("Using avatar hands requires a local avatar prefab. Failure to provide one will result in nothing being displayed. \n\n" +
                 "Note: In order to render avatar hands, you will need to set an app id in OvrAvatarSettings. Any number will do, but it needs to be set.")]
        private bool renderAvatarHandsInsteadOfControllers = true;

        /// <summary>
        /// Using avatar hands requires a local avatar prefab. Failure to provide one will result in nothing being displayed.
        /// </summary>
        public bool RenderAvatarHandsInsteadOfController => renderAvatarHandsInsteadOfControllers;

        [Header("Prefab references")]
        [SerializeField]
        [Tooltip("Prefab reference for OVRCameraRig to load, if none are found in scene.")]
        private OVRCameraRig ovrCameraRigPrefab = null;

        /// <summary>
        /// Prefab reference for OVRCameraRig to load, if none are found in scene.
        /// </summary>
        public OVRCameraRig OVRCameraRigPrefab => ovrCameraRigPrefab;

        [SerializeField]
        [Tooltip("Use this if you want to manage the avatar hands prefab yourself.")]
        private bool allowDevToManageAvatarPrefab = false;

        /// <summary>
        /// Use this if you want to manage the avatar hands prefab yourself.
        /// </summary>
        public bool AllowDevToManageAvatarPrefab => allowDevToManageAvatarPrefab;

        [SerializeField]
        [Tooltip("Prefab reference for LocalAvatar to load, if none are found in scene.")]
        private GameObject localAvatarPrefab = null;

        /// <summary>
        /// Prefab reference for LocalAvatar to load, if none are found in scene.
        /// </summary>
        public GameObject LocalAvatarPrefab => localAvatarPrefab;

        [Header("Hand Mesh Visualization")]
        [SerializeField]
        [Tooltip("If true, hand mesh material will be replaced with custom material.")]
        private bool useCustomHandMaterial = true;

        /// <summary>
        /// If true, hand mesh material will be replaced with custom material.
        /// </summary>
        public bool UseCustomHandMaterial => useCustomHandMaterial;

        [SerializeField]
        [Tooltip("Custom hand material to use for hand tracking hand mesh.")]
        private Material customHandMaterial = null;

        /// <summary>
        /// Custom hand material to use for hand tracking hand mesh.
        /// </summary>
        public Material CustomHandMaterial => customHandMaterial;

        [SerializeField]
        [Tooltip("If true, will update material pinch strength using OVR Values.")]
        private bool updateMaterialPinchStrengthValue = true;

        /// <summary>
        /// If true, will update material pinch strength using OVR Values.
        /// </summary>
        public bool UpdateMaterialPinchStrengthValue => UseCustomHandMaterial && updateMaterialPinchStrengthValue;

        [SerializeField]
        [Tooltip("Property in custom material used to visualize pinch strength.")]
        private string pinchStrengthMaterialProperty = "_PressIntensity";

        /// <summary>
        /// Property in custom material used to visualize pinch strength.
        /// </summary>
        public string PinchStrengthMaterialProperty => pinchStrengthMaterialProperty;

        [Header("Hand Tracking Configuration")]
        [SerializeField]
        [Tooltip("Setting this to low means hands will continue to track with low confidence.")]
        private OVRHand.TrackingConfidence _minimumHandConfidence = OVRHand.TrackingConfidence.Low;

        /// <summary>
        /// Setting this to low means hands will continue to track with low confidence.
        /// </summary>
        public OVRHand.TrackingConfidence MinimumHandConfidence
        {
            get => _minimumHandConfidence;
            set => _minimumHandConfidence = value;
        }

        /// <summary>
        /// Current tracking confidence of left hand. Value managed by OculusQuestHand.cs.
        /// </summary>
        public OVRHand.TrackingConfidence CurrentLeftHandTrackingConfidence { get; set; }
        
        /// <summary>
        /// Current tracking confidence of right hand. Value managed by OculusQuestHand.cs.
        /// </summary>
        public OVRHand.TrackingConfidence CurrentRightHandTrackingConfidence { get; set; }

        [SerializeField]
        [Range(0f, 5f)]
        [Tooltip("Time after which low confidence is considered unreliable, and tracking is set to false. Setting this to 0 means low-confidence is always acceptable.")]
        private float _lowConfidenceTimeThreshold = 0.2f;

        /// <summary>
        /// Time after which low confidence is considered unreliable, and tracking is set to false.
        /// </summary>
        public float LowConfidenceTimeThreshold
        {
            get => _lowConfidenceTimeThreshold;
            set => _lowConfidenceTimeThreshold = value;
        }

        [Header("Performance Configuration")]
        [SerializeField]
        [Tooltip("Default CPU performance level (0-2 is documented), (3-5 is undocumented).")]
        [Range(0, 5)]
        private int defaultCpuLevel = 2;

        /// <summary>
        /// Accessor for the Oculus CPU power level.
        /// </summary>
        public int CPULevel
        {
            get => defaultCpuLevel;
            set
            {
                defaultCpuLevel = value;
                ApplyConfiguredPerformanceSettings();
            }
        }

        [SerializeField]
        [Tooltip("Default GPU performance level (0-2 is documented), (3-5 is undocumented).")]
        [Range(0, 5)]
        private int defaultGpuLevel = 2;

        /// <summary>
        /// Accessor for the Oculus GPU power level.
        /// </summary>
        public int GPULevel
        {
            get => defaultGpuLevel;
            set
            {
                defaultGpuLevel = value;
                ApplyConfiguredPerformanceSettings();
            }
        }

        public void ApplyConfiguredPerformanceSettings()
        {
#if !UNITY_EDITOR
            OVRManager.cpuLevel = CPULevel;
            OVRManager.gpuLevel = GPULevel;
#endif
        }
    }
}
