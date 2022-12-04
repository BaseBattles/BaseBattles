using UnityEngine;
using Unity.Netcode;
using Unity.Collections;
using UnityEngine.UI;
using TMPro;
using System;

public class PlayerNetwork : NetworkBehaviour
{
    // Prefab references
    [SerializeField] private GameObject healthBar;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private GameObject sphereToSpawn;
    // Set in inspector variables
    [SerializeField]
    private LayerMask groundLayer;
    [SerializeField]
    private AnimationCurve smoothCurve;
    //Scene references
    private TMP_InputField nameInputField;
    // Network variables
    private NetworkVariable<int> playerHealth = new NetworkVariable<int>(maxHealth,NetworkVariableReadPermission.Everyone,NetworkVariableWritePermission.Owner);
    private NetworkVariable<FixedString64Bytes> playerName = new NetworkVariable<FixedString64Bytes>("",NetworkVariableReadPermission.Everyone,NetworkVariableWritePermission.Owner);
    // Player settings
    private const float moveSpeed = 10f; // Units per second
    private const float rotateSpeed = 90f; // Degrees per second
    private const int maxHealth = 100;
    // Camera settings
    private float cameraDistance = 15f;
    private const float cameraDistanceChangeSpeed = 1f;
    private const float cameraDistanceMin = 5f;
    private const float cameraDistanceMax = 80f;
    private const float cameraRotationSpeed = 3f;
    // Variables
    private float timeToAnimate = 0.25f;
    private Vector3 velocity = Vector3.zero;
    private float cameraRotaion = 180f;

    private void Start(){
        // Get references to scene objects
        nameInputField = GameObject.Find("Name Input Field").GetComponent<TMP_InputField>();
        
    }

    private void Update(){
        UpdateVisuals();

        // Only run the rest of the update code if we own this player
        if (!IsOwner) return;

        if(playerName.Value==""){
            // Set initial name to random number
            playerName.Value = UnityEngine.Random.Range(0,100).ToString();  
        } 
        
        GetInputs();
        MovePlayer();
        MoveToSurface();
        AlignToSurface();
        MoveCamera();
    }

    private void GetInputs() {
        velocity = Vector3.zero;
        // Get damage input
        if (Input.GetKeyDown(KeyCode.Q)) ChangeHealth(-10);
        // Get spawn input
        if (Input.GetKeyDown(KeyCode.E)) SpawnObjectServerRPC();
        // Get movement inputs
        if (Input.GetKey(KeyCode.W)) velocity += transform.forward * moveSpeed;
        if (Input.GetKey(KeyCode.A)) transform.RotateAround(transform.position, GetSurfaceBelow().normal, -1f * rotateSpeed * Time.deltaTime);
        if (Input.GetKey(KeyCode.S)) velocity -= transform.forward * moveSpeed;
        if (Input.GetKey(KeyCode.D)) transform.RotateAround(transform.position, GetSurfaceBelow().normal, +1f * rotateSpeed * Time.deltaTime);
        // Get camera inputs
        if (Input.mouseScrollDelta.y>0f) cameraDistance -= cameraDistanceChangeSpeed;
        if (Input.mouseScrollDelta.y<0f) cameraDistance += cameraDistanceChangeSpeed;
        /*if (Input.GetMouseButton(1))*/ cameraRotaion += Input.GetAxis("Mouse X") * cameraRotationSpeed * Time.deltaTime;
        // Get change name input
        if (Input.GetKeyDown(KeyCode.Return)) playerName.Value = nameInputField.text;
        if (Input.GetKeyDown(KeyCode.L)) AlignToSurface();
        if (Input.GetKeyDown(KeyCode.M)) MoveToSurface();
        
        
    }

    private void MoveCamera(){
        // Set camera distance to between max and min
        cameraDistance = Mathf.Clamp(cameraDistance, cameraDistanceMin, cameraDistanceMax);
        // Find the good placement of the camera, smoothly move there and track the player the whole time
        //Vector3 desiredPosition = transform.position + cameraDistance*(transform.up-transform.forward);
        Vector3 desiredPosition = transform.position + Vector3.up*cameraDistance + Vector3.right * Mathf.Sin(cameraRotaion)*cameraDistance+Vector3.forward * Mathf.Cos(cameraRotaion)*cameraDistance;
        Camera.main.transform.position = Vector3.Lerp(Camera.main.transform.position,desiredPosition,(Time.deltaTime/timeToAnimate));
        Camera.main.transform.LookAt(transform.position);
    }

    private void MovePlayer(){
        transform.position += velocity*Time.deltaTime;
    }

    private void ChangeHealth(int change){
        playerHealth.Value += change;
        // Don't allow the health below 0 or above max
        playerHealth.Value = Mathf.Clamp(playerHealth.Value, 0, maxHealth);
    }

    private void UpdateVisuals() {
        // Set healthbar size
        healthBar.transform.localScale = new Vector3((float)playerHealth.Value / (float)maxHealth, 1f, 1f);
        // Set name and face it to the camera
        nameText.text = playerName.Value.ToString();
        nameText.transform.LookAt(2f * nameText.transform.position - Camera.main.transform.position);
    }

    private Vector3 previousNormal = Vector3.zero;
    private void AlignToSurface(){
        RaycastHit surfaceBelow = GetSurfaceBelow();
        if(previousNormal==surfaceBelow.normal){
            return;
        }
        // Quaternion surfaceRotation = Quaternion.FromToRotation(transform.up, surfaceBelow.normal);
        // transform.rotation = Quaternion.Lerp(transform.rotation, surfaceRotation,(Time.deltaTime/timeToAnimate));


        //Up is just the normal
        Vector3 up = surfaceBelow.normal;
        //Make sure the velocity is normalized
        Vector3 vel = velocity.normalized;
        //Project the two vectors using the dot product
        Vector3 forward = vel - up * Vector3.Dot (vel, up);
 
        //Set the rotation with relative forward and up axes
        transform.rotation = Quaternion.LookRotation (forward.normalized, up);

        previousNormal = surfaceBelow.normal;
    }

    private void MoveToSurface(){
        RaycastHit surfaceBelow = GetSurfaceBelow();
        // Snap the player to the ground
        transform.position =  new Vector3(transform.position.x, surfaceBelow.point.y,transform.position.z);
    }

    private RaycastHit GetSurfaceBelow(){
        Ray ray = new Ray(transform.position+Vector3.up*100f,-Vector3.up);
        RaycastHit info = new RaycastHit();
        Physics.Raycast(ray, out info, 999f, groundLayer);
        return info;
    }

    [ServerRpc]
    private void SpawnObjectServerRPC(){
        Instantiate(sphereToSpawn,transform.position,Quaternion.identity).GetComponent<NetworkObject>().Spawn(true);
    }
}
