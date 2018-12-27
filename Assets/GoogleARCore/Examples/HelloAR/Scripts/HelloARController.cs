//-----------------------------------------------------------------------
// <copyright file="HelloARController.cs" company="Google">
//
// Copyright 2017 Google Inc. All Rights Reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// </copyright>
//-----------------------------------------------------------------------

namespace GoogleARCore.Examples.HelloAR
{
    using System.Collections.Generic;
    using GoogleARCore;
    using GoogleARCore.Examples.Common;
    using UnityEngine;

#if UNITY_EDITOR
    // Set up touch input propagation while using Instant Preview in the editor.
    using Input = InstantPreviewInput;
#endif

    /// <summary>
    /// Controls the HelloAR example.
    /// </summary>
    public class HelloARController : MonoBehaviour
    {
        /// <summary>
        /// The first-person camera being used to render the passthrough camera image (i.e. AR background).
        /// </summary>
        public Camera FirstPersonCamera;

        /// <summary>
        /// A prefab for tracking and visualizing detected planes.
        /// </summary>
        public GameObject DetectedPlanePrefab;

        public GameObject LaprasPrefab;

        /// <summary>
        /// A game object parenting UI for displaying the "searching for planes" snackbar.
        /// </summary>
        public GameObject SearchingForPlaneUI;

        /// <summary>
        /// The rotation in degrees need to apply to model when the Andy model is placed.
        /// </summary>
        private const float k_ModelRotation = 180.0f;

        /// <summary>
        /// A list to hold all planes ARCore is tracking in the current frame. This object is used across
        /// the application to avoid per-frame allocations.
        /// </summary>
        private List<DetectedPlane> m_AllPlanes = new List<DetectedPlane>();

        private List<LaprasInfo> m_AllLapras = new List<LaprasInfo>();

        public Material lineMaterial;

        /// <summary>
        /// True if the app is in the process of quitting due to an ARCore connection error, otherwise false.
        /// </summary>
        private bool m_IsQuitting = false;

        private float time = 0;

        private void OnCollisionEnter(Collision collision)
        {
            var i = m_AllLapras.FindIndex((obj) => obj.lapras == collision.gameObject);
            var l = m_AllLapras[i];
            var now = Time.time;
            if (l.changeDistTime + l.changeDistInterval + 1 < now)
            {
                l.changeDistTime = now;
                var interval = Random.Range(110 / l.rotateForce, 250 / l.rotateForce);
                l.rotateForce = Mathf.Abs(l.rotateForce);
                if (interval > 180 / l.rotateForce)
                {
                    interval -= 180 / l.rotateForce;
                    l.rotateForce *= -1;
                }
                l.changeDistInterval = interval;
            }
            m_AllLapras[i] = l;
        }

        /// <summary>
        /// The Unity Update() method.
        /// </summary>
        public void Update()
        {
            _UpdateApplicationLifecycle();

            time += Time.deltaTime;

            for (int i = 0; i < m_AllLapras.Count; i++)
            {
                var l = m_AllLapras[i];

                var newY = l.initY + Mathf.Sin((l.initTime + time) * 1.5f) * 8f;
                l.lapras.GetComponent<MeshCollider>().sharedMesh = GetCylinderMesh(200, 200, newY);

                float forward = 0;

                if (Time.time < l.changeDistTime + l.changeDistInterval)
                {
                    l.lapras.transform.Rotate(0, l.rotateForce * (1 - Mathf.Sin((l.initTime + time) * 1.5f) * .02f) * Time.deltaTime, 0);
                }
                else
                {
                    forward = l.forwardForce * (1 - Mathf.Sin((l.initTime + time) * 1.5f) * .02f) * Time.deltaTime;
                }
                l.lapras.transform.position += l.lapras.transform.forward.normalized * forward;
            }

            // Hide snackbar when currently tracking at least one plane.
            Session.GetTrackables<DetectedPlane>(m_AllPlanes);
            bool showSearchingUI = true;
            var wallvertiles = new List<Vector3>();
            var wallIndices = new List<int>();

            for (int i = 0; i < m_AllPlanes.Count; i++)
            {
                var p = m_AllPlanes[i];

                if (showSearchingUI && p.TrackingState == TrackingState.Tracking)
                    showSearchingUI = false;

                var wallStartVertileCount = wallvertiles.Count;
                var bound = new List<Vector3>();
                p.GetBoundaryPolygon(bound);
                wallvertiles.AddRange(GetWallVertiles(bound, 1));

                for (int j = 0; j < bound.Count; j++)
                {
                    wallIndices.Add(wallStartVertileCount + j * 2);
                    wallIndices.Add(wallStartVertileCount + j * 2 + 1);
                    wallIndices.Add(wallStartVertileCount + j * 2 + 2);
                    wallIndices.Add(wallStartVertileCount + j * 2 + 3);
                    wallIndices.Add(wallStartVertileCount + j * 2 + 2);
                    wallIndices.Add(wallStartVertileCount + j * 2 + 1);
                }
            }

            var mesh = new Mesh();
            mesh.Clear();
            mesh.SetVertices(wallvertiles);
            mesh.SetTriangles(wallIndices, 0);

            var meshCollider = GetComponent<MeshCollider>();
            if (!meshCollider) meshCollider = gameObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = mesh;

            SearchingForPlaneUI.SetActive(showSearchingUI);

            // If the player has not touched the screen, we are done with this update.
            Touch touch;
            if (Input.touchCount < 1 || (touch = Input.GetTouch(0)).phase != TouchPhase.Began)
            {
                return;
            }

            // Raycast against the location the player touched to search for planes.
            TrackableHit hit;
            TrackableHitFlags raycastFilter = TrackableHitFlags.PlaneWithinPolygon |
               TrackableHitFlags.FeaturePointWithSurfaceNormal;

            if (Frame.Raycast(touch.position.x, touch.position.y, raycastFilter, out hit))
            {
                // Use hit pose and camera pose to check if hittest is from the
                // back of the plane, if it is, no need to create the anchor.
                if ((hit.Trackable is DetectedPlane) &&
                    Vector3.Dot(FirstPersonCamera.transform.position - hit.Pose.position,
                        hit.Pose.rotation * Vector3.up) < 0)
                {
                    Debug.Log("Hit at back of the current DetectedPlane");
                }
                else
                {

                    // Instantiate Lapras model at the hit pose.
                    var lapras = Instantiate(LaprasPrefab, hit.Pose.position, Quaternion.identity);
                    lapras.transform.Rotate(Vector3.up, Random.Range(0f, 360f));
                    lapras.GetComponentsInChildren<SkinnedMeshRenderer>()[0].material.renderQueue = 3020;

                    // Create an anchor to allow ARCore to track the hitpoint as understanding of the physical
                    // world evolves.
                    var anchor = hit.Trackable.CreateAnchor(hit.Pose);

                    // Make Lapras model a child of the anchor.
                    lapras.transform.parent = anchor.transform;
                    m_AllLapras.Add(new LaprasInfo(lapras));
                }
            }

            //var andyObject = Instantiate(LaprasPrefab, FirstPersonCamera.transform.TransformPoint(0, 0, 0), hit.Pose.rotation);
            //andyObject.AddComponent<BoxCollider>();
            //andyObject.AddComponent<Rigidbody>();
            //andyObject.GetComponent<Rigidbody>().AddForce(FirstPersonCamera.transform.TransformDirection(0, 1f, 2f), ForceMode.Impulse);
        }

        /// <summary>
        /// Check and update the application lifecycle.
        /// </summary>
        private void _UpdateApplicationLifecycle()
        {
            // Exit the app when the 'back' button is pressed.
            if (Input.GetKey(KeyCode.Escape))
            {
                Application.Quit();
            }

            // Only allow the screen to sleep when not tracking.
            if (Session.Status != SessionStatus.Tracking)
            {
                const int lostTrackingSleepTimeout = 15;
                Screen.sleepTimeout = lostTrackingSleepTimeout;
            }
            else
            {
                Screen.sleepTimeout = SleepTimeout.NeverSleep;
            }

            if (m_IsQuitting)
            {
                return;
            }

            // Quit if ARCore was unable to connect and give Unity some time for the toast to appear.
            if (Session.Status == SessionStatus.ErrorPermissionNotGranted)
            {
                _ShowAndroidToastMessage("Camera permission is needed to run this application.");
                m_IsQuitting = true;
                Invoke("_DoQuit", 0.5f);
            }
            else if (Session.Status.IsError())
            {
                _ShowAndroidToastMessage("ARCore encountered a problem connecting.  Please start the app again.");
                m_IsQuitting = true;
                Invoke("_DoQuit", 0.5f);
            }
        }

        /// <summary>
        /// Actually quit the application.
        /// </summary>
        private void _DoQuit()
        {
            Application.Quit();
        }

        /// <summary>
        /// Show an Android toast message.
        /// </summary>
        /// <param name="message">Message string to show in the toast.</param>
        private void _ShowAndroidToastMessage(string message)
        {
            AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            AndroidJavaObject unityActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");

            if (unityActivity != null)
            {
                AndroidJavaClass toastClass = new AndroidJavaClass("android.widget.Toast");
                unityActivity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    AndroidJavaObject toastObject = toastClass.CallStatic<AndroidJavaObject>("makeText", unityActivity,
                        message, 0);
                    toastObject.Call("show");
                }));
            }
        }

        static private List<Vector3> GetWallVertiles(List<Vector3> bound, float height)
        {
            var vertiles = new List<Vector3>();
            for (int i = 0; i < bound.Count; i++)
            {
                vertiles.Add(new Vector3(bound[i].x, bound[i].y, bound[i].z));
                vertiles.Add(new Vector3(bound[i].x, bound[i].y + height, bound[i].z));
            }
            vertiles.Add(new Vector3(bound[0].x, bound[0].y, bound[0].z));
            vertiles.Add(new Vector3(bound[0].x, bound[0].y + height, bound[0].z));

            return vertiles;
        }

        static private Mesh GetCylinderMesh(float radius, float height, float bottomY)
        {
            var vertileCount = 36;
            var circle = new List<Vector3>();
            for (int i = 0; i < vertileCount; i++)
            {
                circle.Add(new Vector3(Mathf.Sin(Mathf.PI * 2 * i / vertileCount) * radius, bottomY, Mathf.Cos(Mathf.PI * 2 * i / vertileCount) * radius));
            }

            var vertiles = GetWallVertiles(circle, height);
            var indices = new List<int>();

            for (int i = 0; i < vertileCount; i++)
            {
                indices.Add(i * 2);
                indices.Add(i * 2 + 1);
                indices.Add(i * 2 + 2);
                indices.Add(i * 2 + 3);
                indices.Add(i * 2 + 2);
                indices.Add(i * 2 + 1);
            }
            var mesh = new Mesh();
            mesh.Clear();
            mesh.SetVertices(vertiles);
            mesh.SetTriangles(indices, 0);

            return mesh;
        }

        private struct LaprasInfo
        {
            public GameObject lapras;
            public float initY;
            public float initTime;
            public float forwardForce;
            public float rotateForce;
            public float changeDistTime;
            public float changeDistInterval;

            public LaprasInfo(GameObject lapras)
            {
                this.lapras = lapras;
                this.initY = 40;
                this.initTime = Time.time;
                this.forwardForce = .06f;
                this.rotateForce = 30f;
                this.changeDistTime = 0;
                this.changeDistInterval = 0;
                lapras.GetComponent<MeshCollider>().sharedMesh = GetCylinderMesh(200, 200, initY);
            }
        }
    }
}