﻿using Autodesk.Maya.OpenMaya;
using BabylonExport.Entities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Maya2Babylon
{
    class AnimCurvData
    {
        public string animCurv;
        public Dictionary<int, float> valuePerFrame = new Dictionary<int, float>();
    }

    internal partial class BabylonExporter
    {
        /// <summary>
        /// Export TRS and visiblity animations of the transform
        /// </summary>
        /// <param name="babylonNode"></param>
        /// <param name="mFnTransform">Transform above mesh/camera/light</param>
        private void ExportNodeAnimation(BabylonNode babylonNode, MFnTransform mFnTransform)
        {
            try
            {
                babylonNode.animations = GetAnimation(mFnTransform).ToArray();
                
                // TODO - Retreive from Maya
                babylonNode.autoAnimate = true;
                babylonNode.autoAnimateFrom = GetMinTime()[0];
                babylonNode.autoAnimateTo = GetMaxTime()[0];
                babylonNode.autoAnimateLoop = true;
            }
            catch (Exception e)
            {
                RaiseError("Error while exporting animation: "+ e.Message, 2);
            }
        }

        /// <summary>
        /// Get TRS and visiblity animations of the transform
        /// </summary>
        /// <param name="transform">Transform above mesh/camera/light</param>
        /// <returns></returns>
        private List<BabylonAnimation> GetAnimation(MFnTransform transform)
        {
            // Animations
            MPlugArray connections = new MPlugArray();
            MStringArray animCurvList = new MStringArray();
            MIntArray keysTime = new MIntArray();
            MDoubleArray keysValue = new MDoubleArray();

            MFloatArray translateValues = new MFloatArray();
            MFloatArray rotateValues = new MFloatArray();
            MFloatArray scaleValues = new MFloatArray();
            MFloatArray visibilityValues = new MFloatArray();
            MFloatArray keyTimes = new MFloatArray();

            List<BabylonAnimationKey> keys = new List<BabylonAnimationKey>();
            List<BabylonAnimation> animationsObject = new List<BabylonAnimation>();

            //Get the animCurve
            MGlobal.executeCommand("listConnections -type \"animCurve\" " + transform.fullPathName + ";", animCurvList);

            List<AnimCurvData> animCurvesData = new List<AnimCurvData>();
            foreach (String animCurv in animCurvList)
            {
                AnimCurvData animCurvData = new AnimCurvData();
                animCurvesData.Add(animCurvData);

                animCurvData.animCurv = animCurv;

                //Get the key time for each curves
                MGlobal.executeCommand("keyframe -q " + animCurv + ";", keysTime);

                //Get the value for each curves
                MGlobal.executeCommand("keyframe - q -vc -absolute " + animCurv + ";", keysValue);

                if (animCurv.EndsWith("translateZ") || animCurv.EndsWith("rotateX") || animCurv.EndsWith("rotateY"))
                {
                    for (int index = 0; index < keysTime.Count; index++)
                    {
                        // Switch coordinate system at object level
                        animCurvData.valuePerFrame.Add(keysTime[index], (float)keysValue[index] * -1.0f);
                    }
                }
                else
                {
                    for (int index = 0; index < keysTime.Count; index++)
                    {
                        animCurvData.valuePerFrame.Add(keysTime[index], (float)keysValue[index]);
                    }
                }
            }

            string[] mayaAnimationProperties = new string[] { "translate", "rotate", "scale" };
            string[] babylonAnimationProperties = new string[] { "position", "rotationQuaternion", "scaling" };
            string[] axis = new string[] { "X", "Y", "Z" };

            // Init TRS default values
            Dictionary<string, float> defaultValues = new Dictionary<string, float>();
            float[] position = null;
            float[] rotationQuaternion = null;
            float[] rotation = null;
            float[] scaling = null;
            GetTransform(transform, ref position, ref rotationQuaternion, ref rotation, ref scaling); // coordinate system already switched
            defaultValues.Add("translateX", position[0]);
            defaultValues.Add("translateY", position[1]);
            defaultValues.Add("translateZ", position[2]);
            defaultValues.Add("rotateX", rotation[0]);
            defaultValues.Add("rotateY", rotation[1]);
            defaultValues.Add("rotateZ", rotation[2]);
            defaultValues.Add("scaleX", scaling[0]);
            defaultValues.Add("scaleY", scaling[1]);
            defaultValues.Add("scaleZ", scaling[2]);
            
            for (int indexAnimationProperty = 0; indexAnimationProperty < mayaAnimationProperties.Length; indexAnimationProperty++)
            {
                string mayaAnimationProperty = mayaAnimationProperties[indexAnimationProperty];

                // Retreive animation curves data for current animation property
                // Ex: all "translate" data are "translateX", "translateY", "translateZ"
                List<AnimCurvData> animDataProperty = animCurvesData.Where(data => data.animCurv.Contains(mayaAnimationProperty)).ToList();

                if (animDataProperty.Count == 0)
                {
                    // Property is not animated
                    continue;
                }

                // Get all frames for this property
                List<int> framesProperty = new List<int>();
                foreach (var animData in animDataProperty)
                {
                    framesProperty.AddRange(animData.valuePerFrame.Keys);
                }
                framesProperty = framesProperty.Distinct().ToList();
                framesProperty.Sort();

                // Get default values for this property
                BabylonAnimationKey lastBabylonAnimationKey = new BabylonAnimationKey();
                lastBabylonAnimationKey.frame = 0;
                lastBabylonAnimationKey.values = new float[] { defaultValues[mayaAnimationProperty + "X"], defaultValues[mayaAnimationProperty + "Y"], defaultValues[mayaAnimationProperty + "Z"] };

                // Compute all values for this property
                List<BabylonAnimationKey> babylonAnimationKeys = new List<BabylonAnimationKey>();
                foreach (var frameProperty in framesProperty)
                {
                    BabylonAnimationKey babylonAnimationKey = new BabylonAnimationKey();
                    babylonAnimationKeys.Add(babylonAnimationKey);

                    // Frame
                    babylonAnimationKey.frame = frameProperty;

                    // Values
                    float[] valuesProperty = new float[3];
                    for (int indexAxis = 0; indexAxis < axis.Length; indexAxis++)
                    {
                        AnimCurvData animCurvDataAxis = animDataProperty.Find(data => data.animCurv.EndsWith(axis[indexAxis]));

                        float value;
                        if (animCurvDataAxis != null && animCurvDataAxis.valuePerFrame.ContainsKey(frameProperty))
                        {
                            value = animCurvDataAxis.valuePerFrame[frameProperty];
                        }
                        else
                        {
                            value = lastBabylonAnimationKey.values[indexAxis];
                        }
                        valuesProperty[indexAxis] = value;
                    }
                    babylonAnimationKey.values = valuesProperty.ToArray();

                    // Update last known values
                    lastBabylonAnimationKey = babylonAnimationKey;
                }

                // Optimization
                OptimizeAnimations(babylonAnimationKeys, true);

                // Convert euler to quaternion angles
                if (indexAnimationProperty == 1) // Rotation
                {
                    foreach (var babylonAnimationKey in babylonAnimationKeys)
                    {
                        BabylonVector3 eulerAngles = BabylonVector3.FromArray(babylonAnimationKey.values);
                        BabylonQuaternion quaternionAngles = eulerAngles.toQuaternion();
                        babylonAnimationKey.values = quaternionAngles.ToArray();
                    }
                }

                // Ensure animation has at least 2 frames
                if (babylonAnimationKeys.Count > 1)
                {
                    var animationPresent = true;

                    // Ensure animation has at least 2 non equal frames
                    if (babylonAnimationKeys.Count == 2)
                    {
                        if (babylonAnimationKeys[0].values.IsEqualTo(babylonAnimationKeys[1].values))
                        {
                            animationPresent = false;
                        }
                    }

                    if (animationPresent)
                    {
                        // Create BabylonAnimation
                        string babylonAnimationProperty = babylonAnimationProperties[indexAnimationProperty];
                        animationsObject.Add(new BabylonAnimation()
                        {
                            dataType = indexAnimationProperty == 1 ? (int)BabylonAnimation.DataType.Quaternion : (int)BabylonAnimation.DataType.Vector3,
                            name = babylonAnimationProperty + " animation",
                            framePerSecond = 30,
                            loopBehavior = 1,
                            property = babylonAnimationProperty,
                            keys = babylonAnimationKeys.ToArray()
                        });
                    }
                }
            }

            return animationsObject;
        }

        static void OptimizeAnimations(List<BabylonAnimationKey> keys, bool removeLinearAnimationKeys)
        {
            for (int ixFirst = keys.Count - 3; ixFirst >= 0; --ixFirst)
            {
                while (keys.Count - ixFirst >= 3)
                {
                    if (!RemoveAnimationKey(keys, ixFirst, removeLinearAnimationKeys))
                    {
                        break;
                    }
                }
            }
        }

        static float[] weightedLerp(int frame0, int frame1, int frame2, float[] value0, float[] value2)
        {
            double weight2 = (frame1 - frame0) / (double)(frame2 - frame0);
            double weight0 = 1 - weight2;
            float[] result = new float[value0.Length];
            for (int i = 0; i < result.Length; ++i)
            {
                result[i] = (float)(value0[i] * weight0 + value2[i] * weight2);
            }
            return result;
        }

        private static bool RemoveAnimationKey(List<BabylonAnimationKey> keys, int ixFirst, bool removeLinearAnimationKeys)
        {
            var first = keys[ixFirst];
            var middle = keys[ixFirst + 1];
            var last = keys[ixFirst + 2];

            // first pass, frame equality
            if (first.values.IsEqualTo(last.values) && first.values.IsEqualTo(middle.values))
            {
                keys.RemoveAt(ixFirst + 1);
                return true;
            }

            // second pass : linear interpolation detection
            if (removeLinearAnimationKeys)
            {
                var computedMiddleValue = weightedLerp(first.frame, middle.frame, last.frame, first.values, last.values);
                if (computedMiddleValue.IsEqualTo(middle.values))
                {
                    keys.RemoveAt(ixFirst + 1);
                    return true;
                }
            }
            return false;

        }

        private MIntArray GetMinTime()
        {
            MIntArray minTime = new MIntArray();
            MGlobal.executeCommand("playbackOptions -q -minTime", minTime);
            return minTime;
        }

        private MIntArray GetMaxTime()
        {
            MIntArray maxTime = new MIntArray();
            MGlobal.executeCommand("playbackOptions -q -maxTime", maxTime);
            return maxTime;
        }
    }
}