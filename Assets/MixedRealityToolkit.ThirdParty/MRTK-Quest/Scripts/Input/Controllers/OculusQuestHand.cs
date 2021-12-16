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

using Microsoft.MixedReality.Toolkit;
using Microsoft.MixedReality.Toolkit.Input;
using Microsoft.MixedReality.Toolkit.Utilities;
using System.Collections.Generic;
using prvncher.MixedReality.Toolkit.Config;
using UnityEngine;
using static OVRSkeleton;

namespace prvncher.MixedReality.Toolkit.OculusQuestInput
{
    [MixedRealityController(SupportedControllerType.ArticulatedHand, new[] { Handedness.Left, Handedness.Right })]
    public class OculusQuestHand : BaseHand, IMixedRealityHand
    {
        private MixedRealityPose currentPointerPose = MixedRealityPose.ZeroIdentity;

        /// <summary>
        /// Pose used by hand ray
        /// </summary>
        public MixedRealityPose HandPointerPose => currentPointerPose;

        private MixedRealityPose currentIndexPose = MixedRealityPose.ZeroIdentity;
        private MixedRealityPose currentGripPose = MixedRealityPose.ZeroIdentity;

        // Use Kalman filters to improve palm and index positions, as they drive many interactions
        private KalmanFilterVector3 palmFilter = new KalmanFilterVector3();
        private KalmanFilterVector3 indexTipFilter = new KalmanFilterVector3();

        private readonly Material handMaterial;
        private Renderer handRenderer;

        // TODO: Hand mesh
        // private int[] handMeshTriangleIndices = null;
        // private Vector2[] handMeshUVs;

        private int pinchStrengthProp;

        /// <summary>
        /// Default constructor used by reflection for profiles
        /// </summary>
        /// <param name="trackingState"></param>
        /// <param name="controllerHandedness"></param>
        /// <param name="inputSource"></param>
        /// <param name="interactions"></param>
        public OculusQuestHand(TrackingState trackingState, Handedness controllerHandedness, IMixedRealityInputSource inputSource = null, MixedRealityInteractionMapping[] interactions = null)
            : base(trackingState, controllerHandedness, inputSource, interactions)
        {
            palmFilter.Reset();
            indexTipFilter.Reset();
        }

        public OculusQuestHand(TrackingState trackingState, Handedness controllerHandedness, OVRHand ovrHand, Material handMaterial, IMixedRealityInputSource inputSource = null, MixedRealityInteractionMapping[] interactions = null)
            : base(trackingState, controllerHandedness, inputSource, interactions)
        {
            palmFilter.Reset();
            indexTipFilter.Reset();
            this.handMaterial = handMaterial;
            handRenderer = ovrHand.GetComponent<Renderer>();

            if (!MRTKOculusConfig.Instance.UseCustomHandMaterial) return;

            handRenderer.sharedMaterial = handMaterial;
            if (MRTKOculusConfig.Instance.UpdateMaterialPinchStrengthValue)
            {
                pinchStrengthProp = Shader.PropertyToID(MRTKOculusConfig.Instance.PinchStrengthMaterialProperty);
            }
        }

        public void CleanupHand()
        {
            if (handRenderer != null)
            {
                handRenderer.enabled = false;
            }
        }

        public override MixedRealityInteractionMapping[] DefaultInteractions => new[]
        {
            new MixedRealityInteractionMapping(0, "Spatial Pointer", AxisType.SixDof, DeviceInputType.SpatialPointer, new MixedRealityInputAction(4, "Pointer Pose", AxisType.SixDof)),
            new MixedRealityInteractionMapping(1, "Spatial Grip", AxisType.SixDof, DeviceInputType.SpatialGrip, new MixedRealityInputAction(3, "Grip Pose", AxisType.SixDof)),
            new MixedRealityInteractionMapping(2, "Select", AxisType.Digital, DeviceInputType.Select, new MixedRealityInputAction(1, "Select", AxisType.Digital)),
            new MixedRealityInteractionMapping(3, "Grab", AxisType.SingleAxis, DeviceInputType.TriggerPress, new MixedRealityInputAction(7, "Grip Press", AxisType.SingleAxis)),
            new MixedRealityInteractionMapping(4, "Index Finger Pose", AxisType.SixDof, DeviceInputType.IndexFinger,  new MixedRealityInputAction(13, "Index Finger Pose", AxisType.SixDof)),
        };

        public override MixedRealityInteractionMapping[] DefaultLeftHandedInteractions => DefaultInteractions;

        public override MixedRealityInteractionMapping[] DefaultRightHandedInteractions => DefaultInteractions;

        public override void SetupDefaultInteractions(Handedness controllerHandedness)
        {
            AssignControllerMappings(DefaultInteractions);
        }

        #region IMixedRealityHand Implementation

        /// <inheritdoc/>
        public override bool TryGetJoint(TrackedHandJoint joint, out MixedRealityPose pose)
        {
            return jointPoses.TryGetValue(joint, out pose);
        }

        #endregion IMixedRealityHand Implementation

        public override bool IsInPointingPose
        {
            get
            {
                if (!TryGetJoint(TrackedHandJoint.Palm, out var palmPose)) return false;

                Transform cameraTransform = CameraCache.Main.transform;

                Vector3 projectedPalmUp = Vector3.ProjectOnPlane(-palmPose.Up, cameraTransform.up);

                // We check if the palm forward is roughly in line with the camera lookAt
                return Vector3.Dot(cameraTransform.forward, projectedPalmUp) > 0.3f;
            }
        }

        protected bool IsPinching { set; get; }

        /// <summary>
        /// Update the controller data from the provided platform state
        /// </summary>
        /// <param name="interactionSourceState">The InteractionSourceState retrieved from the platform</param>
        public void UpdateController(OVRHand hand, OVRSkeleton ovrSkeleton, Transform trackingOrigin)
        {
            if (!Enabled || hand == null || ovrSkeleton == null)
            {
                return;
            }

            bool isTracked = UpdateHandData(hand, ovrSkeleton);
            IsPositionAvailable = IsRotationAvailable = isTracked;

            if (isTracked)
            {
                // Leverage Oculus Platform Hand Ray - instead of simulating it in a crummy way
                currentPointerPose.Position = trackingOrigin.TransformPoint(hand.PointerPose.position);

                Vector3 pointerForward = trackingOrigin.TransformDirection(hand.PointerPose.forward);
                Vector3 pointerUp = trackingOrigin.TransformDirection(hand.PointerPose.up);

                currentPointerPose.Rotation = Quaternion.LookRotation(pointerForward, pointerUp);

                if (TryGetJoint(TrackedHandJoint.Palm, out var palmPose))
                {
                    currentGripPose = palmPose;
                }

                CoreServices.InputSystem?.RaiseSourcePoseChanged(InputSource, this, currentGripPose);

                UpdateVelocity();
            }

            for (int i = 0; i < Interactions?.Length; i++)
            {
                switch (Interactions[i].InputType)
                {
                    case DeviceInputType.SpatialPointer:
                        Interactions[i].PoseData = currentPointerPose;
                        if (Interactions[i].Changed)
                        {
                            CoreServices.InputSystem?.RaisePoseInputChanged(InputSource, ControllerHandedness, Interactions[i].MixedRealityInputAction, currentPointerPose);
                        }
                        break;
                    case DeviceInputType.SpatialGrip:
                        Interactions[i].PoseData = currentGripPose;
                        if (Interactions[i].Changed)
                        {
                            CoreServices.InputSystem?.RaisePoseInputChanged(InputSource, ControllerHandedness, Interactions[i].MixedRealityInputAction, currentGripPose);
                        }
                        break;
                    case DeviceInputType.Select:
                        Interactions[i].BoolData = IsPinching;

                        if (Interactions[i].Changed)
                        {
                            if (Interactions[i].BoolData)
                            {
                                CoreServices.InputSystem?.RaiseOnInputDown(InputSource, ControllerHandedness, Interactions[i].MixedRealityInputAction);
                            }
                            else
                            {
                                CoreServices.InputSystem?.RaiseOnInputUp(InputSource, ControllerHandedness, Interactions[i].MixedRealityInputAction);
                            }
                        }
                        break;
                    case DeviceInputType.TriggerPress:
                        Interactions[i].BoolData = IsPinching;

                        if (Interactions[i].Changed)
                        {
                            if (Interactions[i].BoolData)
                            {
                                CoreServices.InputSystem?.RaiseOnInputDown(InputSource, ControllerHandedness, Interactions[i].MixedRealityInputAction);
                            }
                            else
                            {
                                CoreServices.InputSystem?.RaiseOnInputUp(InputSource, ControllerHandedness, Interactions[i].MixedRealityInputAction);
                            }
                        }
                        break;
                    case DeviceInputType.IndexFinger:
                        UpdateIndexFingerData(Interactions[i]);
                        break;
                }
            }
        }

        #region HandJoints
        protected readonly Dictionary<TrackedHandJoint, MixedRealityPose> jointPoses = new Dictionary<TrackedHandJoint, MixedRealityPose>();

        protected readonly Dictionary<BoneId, TrackedHandJoint> boneJointMapping = new Dictionary<BoneId, TrackedHandJoint>()
        {
            { BoneId.Hand_Thumb1, TrackedHandJoint.ThumbMetacarpalJoint },
            { BoneId.Hand_Thumb2, TrackedHandJoint.ThumbProximalJoint },
            { BoneId.Hand_Thumb3, TrackedHandJoint.ThumbDistalJoint },
            { BoneId.Hand_ThumbTip, TrackedHandJoint.ThumbTip },
            { BoneId.Hand_Index1, TrackedHandJoint.IndexKnuckle },
            { BoneId.Hand_Index2, TrackedHandJoint.IndexMiddleJoint },
            { BoneId.Hand_Index3, TrackedHandJoint.IndexDistalJoint },
            { BoneId.Hand_IndexTip, TrackedHandJoint.IndexTip },
            { BoneId.Hand_Middle1, TrackedHandJoint.MiddleKnuckle },
            { BoneId.Hand_Middle2, TrackedHandJoint.MiddleMiddleJoint },
            { BoneId.Hand_Middle3, TrackedHandJoint.MiddleDistalJoint },
            { BoneId.Hand_MiddleTip, TrackedHandJoint.MiddleTip },
            { BoneId.Hand_Ring1, TrackedHandJoint.RingKnuckle },
            { BoneId.Hand_Ring2, TrackedHandJoint.RingMiddleJoint },
            { BoneId.Hand_Ring3, TrackedHandJoint.RingDistalJoint },
            { BoneId.Hand_RingTip, TrackedHandJoint.RingTip },
            { BoneId.Hand_Pinky1, TrackedHandJoint.PinkyKnuckle },
            { BoneId.Hand_Pinky2, TrackedHandJoint.PinkyMiddleJoint },
            { BoneId.Hand_Pinky3, TrackedHandJoint.PinkyDistalJoint },
            { BoneId.Hand_PinkyTip, TrackedHandJoint.PinkyTip },
            { BoneId.Hand_WristRoot, TrackedHandJoint.Wrist },
        };

        private float _lastHighConfidenceTime = 0f;
        protected bool UpdateHandData(OVRHand ovrHand, OVRSkeleton ovrSkeleton)
        {
            bool isTracked = ovrHand.IsTracked;
            if (ovrHand.HandConfidence == OVRHand.TrackingConfidence.High)
            {
                _lastHighConfidenceTime = Time.unscaledTime;
            }
            if (ovrHand.HandConfidence == OVRHand.TrackingConfidence.Low)
            {
                if (MRTKOculusConfig.Instance.MinimumHandConfidence == OVRHand.TrackingConfidence.High)
                {
                    isTracked = false;
                }
                else
                {
                    float lowConfidenceTime = Time.time - _lastHighConfidenceTime;
                    if (MRTKOculusConfig.Instance.LowConfidenceTimeThreshold > 0 &&
                        MRTKOculusConfig.Instance.LowConfidenceTimeThreshold < lowConfidenceTime)
                    {
                        isTracked = false;
                    }
                }
            }

            if (ControllerHandedness == Handedness.Left)
            {
                MRTKOculusConfig.Instance.CurrentLeftHandTrackingConfidence = ovrHand.HandConfidence;
            }
            else
            {
                MRTKOculusConfig.Instance.CurrentRightHandTrackingConfidence = ovrHand.HandConfidence;
            }

            // Disable hand if not tracked
            if (handRenderer != null)
            {
                handRenderer.enabled = isTracked;
            }

            if (ovrSkeleton != null)
            {
                var bones = ovrSkeleton.Bones;
                foreach (var bone in bones)
                {
                    UpdateBone(bone);
                }

                UpdatePalm();
            }

            CoreServices.InputSystem?.RaiseHandJointsUpdated(InputSource, ControllerHandedness, jointPoses);

            // Note: After some testing, it seems when moving your hand fast, Oculus's pinch estimation data gets frozen, which leads to stuck pinches.
            // To counter this, we perform a distance check between thumb and index to determine if we should force the pinch to a false state.
            float pinchStrength;
            if (AreIndexAndThumbFarApart())
            {
                pinchStrength = 0f;
                IsPinching = false;
            }
            else
            {
                pinchStrength = ovrHand.GetFingerPinchStrength(OVRHand.HandFinger.Index);
                if (IsPinching)
                {
                    // If we are already pinching, we make the pinch a bit sticky
                    IsPinching = ovrHand.GetFingerPinchStrength(OVRHand.HandFinger.Index) > 0.85f;
                }
                else
                {
                    // If not yet pinching, only consider pinching if finger confidence is high
                    IsPinching = ovrHand.GetFingerIsPinching(OVRHand.HandFinger.Index)
                                 && ovrHand.GetFingerConfidence(OVRHand.HandFinger.Index) == OVRHand.TrackingConfidence.High;
                }
            }

            if (MRTKOculusConfig.Instance.UpdateMaterialPinchStrengthValue && handMaterial != null)
            {
                handMaterial.SetFloat(pinchStrengthProp, pinchStrength);
            }
            return isTracked;
        }

        // 4 cm is the treshold for fingers being far apart.
        // 0.0016 is the square magnitude equivalent
        // Square magnitude is less expensive to perform than a distance check
        private const float IndexThumbSqrMagnitudeThreshold = 0.0016f;
        private bool AreIndexAndThumbFarApart()
        {
            MixedRealityPose indexPose = MixedRealityPose.ZeroIdentity;
            TryGetJoint(TrackedHandJoint.IndexTip, out indexPose);

            MixedRealityPose thumbPose = MixedRealityPose.ZeroIdentity;
            TryGetJoint(TrackedHandJoint.ThumbTip, out thumbPose);

            Vector3 distanceVector = indexPose.Position - thumbPose.Position;
            return distanceVector.sqrMagnitude > IndexThumbSqrMagnitudeThreshold;
        }

        protected void UpdateBone(OVRBone bone)
        {
            var boneId = bone.Id;
            var boneTransform = bone.Transform;

            if (boneJointMapping.TryGetValue(boneId, out var joint))
            {
                Quaternion boneRotation = bone.Transform.rotation;

                // WARNING THIS CODE IS SUBJECT TO CHANGE WITH THE OCULUS SDK - This fix is a hack to fix broken and inconsistent rotations for hands
                if (ControllerHandedness == Handedness.Left)
                {
                    // Rotate palm 180 on X to flip up
                    boneRotation *= Quaternion.Euler(180f, 0f, 0f);

                    // Rotate palm 90 degrees on y to align x with right
                    boneRotation *= Quaternion.Euler(0f, -90, 0f);
                }
                else
                {
                    // Right Up direction is correct

                    // Rotate palm 90 degrees on y to align x with right
                    boneRotation *= Quaternion.Euler(0f, 90f, 0f);
                }

                UpdateJointPose(joint, boneTransform.position, boneRotation);
            }
        }

        protected void UpdatePalm()
        {
            bool hasMiddleKnuckle = TryGetJoint(TrackedHandJoint.MiddleKnuckle, out var middleKnucklePose);
            bool hasWrist = TryGetJoint(TrackedHandJoint.Wrist, out var wristPose);

            if (hasMiddleKnuckle && hasWrist)
            {
                Vector3 wristRootPosition = wristPose.Position;
                Vector3 middle3Position = middleKnucklePose.Position;

                Vector3 palmPosition = Vector3.Lerp(wristRootPosition, middle3Position, 0.5f);
                Quaternion palmRotation = wristPose.Rotation;

                UpdateJointPose(TrackedHandJoint.Palm, palmPosition, palmRotation);
            }
        }

        protected void UpdateJointPose(TrackedHandJoint joint, Vector3 position, Quaternion rotation)
        {
            Vector3 jointPosition = position;

            // TODO Figure out kalman filter coefficients to get good quality smoothing
            /*
            if (joint == TrackedHandJoint.IndexTip)
            {
                jointPosition = indexTipFilter.Update(position);
            }
            else if (joint == TrackedHandJoint.Palm)
            {
                jointPosition = palmFilter.Update(position);
            }
            */

            MixedRealityPose pose = new MixedRealityPose(jointPosition, rotation);
            if (!jointPoses.ContainsKey(joint))
            {
                jointPoses.Add(joint, pose);
            }
            else
            {
                jointPoses[joint] = pose;
            }
        }

        private void UpdateIndexFingerData(MixedRealityInteractionMapping interactionMapping)
        {
            if (jointPoses.TryGetValue(TrackedHandJoint.IndexTip, out var pose))
            {
                currentIndexPose.Rotation = pose.Rotation;
                currentIndexPose.Position = pose.Position;
            }

            interactionMapping.PoseData = currentIndexPose;

            // If our value changed raise it.
            if (interactionMapping.Changed)
            {
                // Raise input system Event if it enabled
                CoreServices.InputSystem?.RaisePoseInputChanged(InputSource, ControllerHandedness, interactionMapping.MixedRealityInputAction, currentIndexPose);
            }
        }

        #endregion
    }
}
