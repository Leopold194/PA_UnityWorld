using UnityEngine;

public class UIManager : MonoBehaviour
{
    public GameObject panelMenuPrincipal;
    public GameObject panelVisualisation;
    public GameObject panelJouer;
    public GameObject panelLoading;
    public GameObject panelTeams;
    public GameObject canvasScore;
    public GameObject canvasMenus;

    public void Start()
    {
        ShowMainMenu();
    }

    public void ShowMainMenu()
    {
        Debug.Log("UIManager: ShowMainMenu");
        canvasMenus.SetActive(true);
        panelMenuPrincipal.SetActive(true);
        panelVisualisation.SetActive(false);
        panelJouer.SetActive(false);
        canvasScore.SetActive(false);
        panelLoading.SetActive(false);
        panelTeams.SetActive(false);
    }

    public void ChooseVisualisationMenu()
    {
        GameChoice.Selected = GameChoice.Mode.Visualisation;
        panelMenuPrincipal.SetActive(false);
        panelVisualisation.SetActive(true);
    }

    public void BackToMenu()
    {
        panelVisualisation.SetActive(false);
        panelJouer.SetActive(false);
        panelMenuPrincipal.SetActive(true);
        GameChoice.Selected = GameChoice.Mode.None;
    }
}