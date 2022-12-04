using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    [SerializeField] private GameObject gameHUD, mainMenu;
    [SerializeField] private Text stateText;

    private void Start()
    {
        // Subscribe to events
        GameManager.Instance.MatchFound += MatchFound;
        GameManager.Instance.UpdateState += UpdateState;
    }

    private void UpdateState(string newState)
    {
        stateText.text = newState;
    }

    private void MatchFound()
    {
        ToggleMenu(false);
    }

    private void OnDestroy()
    {
        // Unsubscribe from events
        GameManager.Instance.MatchFound -= MatchFound;
        GameManager.Instance.UpdateState -= UpdateState;
    }

    public void ToggleMenu(bool mainMenuActive){
        mainMenu.SetActive(mainMenuActive);
        gameHUD.SetActive(!mainMenuActive);
    }
}
