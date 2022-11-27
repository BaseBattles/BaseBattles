using UnityEngine;
using Unity.Netcode;
using Unity.Collections;
using UnityEngine.UI;
using TMPro;

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
    private float cameraDistance = 10f;
    private const float cameraDistanceChangeSpeed = 1f;
    private const float cameraDistanceMin = 5f;
    private const float cameraDistanceMax = 80f;
    // Variables
    private float timeToAnimate = 0.25f;

    private void Start(){
        // Get references to scene objects
        nameInputField = GameObject.Find("Name Input Field").GetComponent<TMP_InputField>();
        // Set initial name to random number
        playerName.Value = Random.Range(0,100).ToString();
    }

    private void Update(){
        UpdateVisuals();

        // Only run the rest of the update code if we own this player
        if (!IsOwner) return;

        GetInputs();
        AlignToSurface();
        MoveCamera();
    }

    private void MoveCamera(){
        // Set camera distance to between max and min
        cameraDistance = Mathf.Clamp(cameraDistance, cameraDistanceMin, cameraDistanceMax);
        // Find the good placement of the camera, smoothly move there and track the player the whole time
        Vector3 desiredPosition = transform.position + cameraDistance*(transform.up-transform.forward);
        Camera.main.transform.position = Vector3.Lerp(Camera.main.transform.position,desiredPosition,(Time.deltaTime/timeToAnimate));
        Camera.main.transform.LookAt(transform.position);
    }

    private void GetInputs() {
        // Get damage input
        if (Input.GetKeyDown(KeyCode.Q)) ChangeHealth(-10);
        // Get spawn input
        if (Input.GetKeyDown(KeyCode.E)) SpawnObjectServerRPC();
        // Get movement inputs
        if (Input.GetKey(KeyCode.W)) transform.position += transform.forward * moveSpeed * Time.deltaTime;
        if (Input.GetKey(KeyCode.A)) transform.Rotate(0f, -1f * rotateSpeed * Time.deltaTime, 0f);
        if (Input.GetKey(KeyCode.S)) transform.position -= transform.forward * moveSpeed * Time.deltaTime;
        if (Input.GetKey(KeyCode.D)) transform.Rotate(0f, rotateSpeed * Time.deltaTime, 0f);
        // Get camera distance inputs
        if (Input.mouseScrollDelta.y>0f) cameraDistance -= cameraDistanceChangeSpeed;
        if (Input.mouseScrollDelta.y<0f) cameraDistance += cameraDistanceChangeSpeed;
        // Get change name input
        if (Input.GetKeyDown(KeyCode.Return)) playerName.Value = nameInputField.text;
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

    private void AlignToSurface(){
        Quaternion rotationRef = transform.rotation;

        Ray ray = new Ray(transform.position+Vector3.up*100f,-transform.up);
        RaycastHit info = new RaycastHit();
        if(Physics.Raycast(ray, out info, 999f, groundLayer)){
            // Find the desired rotation and smoothly rotate to it
            Quaternion desiredRotation = Quaternion.FromToRotation(transform.up, info.normal);
        //   transform.rotation = Quaternion.Lerp(transform.rotation, desiredRotation ,(Time.deltaTime/timeToAnimate));
            // Reset the rotations we want to preserve
         //   transform.rotation = Quaternion.Euler(transform.eulerAngles.x,rotationRef.eulerAngles.y,rotationRef.eulerAngles.z);
            // Snap the player to the ground
            transform.position =  new Vector3(transform.position.x, info.point.y,transform.position.z);
        }

    }

    [ServerRpc]
    private void SpawnObjectServerRPC(){
        Instantiate(sphereToSpawn,transform.position,Quaternion.identity).GetComponent<NetworkObject>().Spawn(true);
    }
}
