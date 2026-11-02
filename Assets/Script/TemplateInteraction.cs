using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Oculus.Interaction.Input;

public class A1Interaction : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    [SerializeField]
    private Hand leftHand; // TO DO: assign OVRLeftHandDataSource to this in the inspector
    [SerializeField]
    private Hand rightHand; // TO DO: assign OVRRightHandDataSource to this in the inspector

    [SerializeField]
    private TextMeshProUGUI LeftHandPosText; // TO DO: assign a TextMeshPro object in the inspector

    [SerializeField]
    private TextMeshProUGUI RightHandPosText; // TO DO: assign a TextMeshPro object in the inspector

    [SerializeField]
    private TextMeshProUGUI StateText; // TO DO: assign a TextMeshPro object in the inspector

    private Pose leftHandPose;
    private Pose rightHandPose;
    private HandJointId handJointId = HandJointId.HandIndex3; // Note: you can change this to any bone you want, such as HandThumbTip, HandMiddleTip, etc.

    [SerializeField]
    private float distanceThreshold = 0.1f; // Distance threshold in meters (10 cm)

    void Update()
    {
        // 1. Get and display position & rotation of left hand joint
        if (leftHand != null && leftHand.GetJointPose(handJointId, out leftHandPose))
        {
            LeftHandPosText.text = $"Left Joint Pos: {leftHandPose.position:F2}\nRot: {leftHandPose.rotation.eulerAngles:F1}";
        }

        // 2. Get and display position & rotation of right hand joint
        if (rightHand != null && rightHand.GetJointPose(handJointId, out rightHandPose))
        {
            RightHandPosText.text = $"Right Joint Pos: {rightHandPose.position:F2}\nRot: {rightHandPose.rotation.eulerAngles:F1}";
        }

        // 3. Detect distance between left and right hand joints
        if (leftHand != null && rightHand != null)
        {
            float distance = Vector3.Distance(leftHandPose.position, rightHandPose.position);

            if (distance < distanceThreshold)
            {
                StateText.text = $"Hands are Close! ({distance:F2}m)";
            }
            else
            {
                StateText.text = $"Distance: {distance:F2}m";
            }
        }
    }

    // 4. Called when ThumbsUp gesture starts
    public void ThumbsUp()
    {
        StateText.text = "Gesture: Thumbs Up Detected!";
    }

    // 5. Called when ThumbsUp gesture ends
    public void NoThumbsUp()
    {
        StateText.text = "Gesture: No Thumbs Up";
    }
}