using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.IO;

/*
    DF_Final_UIManager.cs
    * This is a script to help manage the User Interface in the scene "distancefields_render_test_interactive_final".
        (e.g. a function gets called when the user clicks a button in the UI, or text in the UI is updated when an event happens)
    * If you don't need the UI, this script can be safely ignored or removed.
 */

public class DF_Final_UIManager : MonoBehaviour
{

    private bool gameStopped = true;
    private float lastClick = 0f;

    public GameObject ui_top_panel;
    public GameObject ui_bottom_panel;

    public GameObject quad_mat_in;
    public GameObject quad_mat_out;
    public GameObject quad_mat_debug;
    private Renderer ren_in;
    private Renderer ren_out;
    private Renderer ren_debug;

    // reference to Manager script that generates / renders Distance Fields
    public DF_Final_DistanceFieldManager dfManager;

    private TMP_Dropdown dropdown_select_input;
    private Toggle[] toggle_list_dfsize;
    private Toggle[] toggle_list_olcolor;
    private Toggle[,] toggle_render_layers;
    private Toggle toggle_process_outline;
    private Toggle toggle_process_background;
    private Toggle toggle_render_outline;
    private Toggle toggle_render_background;
    private Toggle toggle_bilinear_filter;
    private Toggle toggle_colorspace_sRGB;
    private Toggle toggle_mat_aa;
    private Slider slider_outline_cutoff;
    private Slider slider_region_cutoff;
    private Slider slider_blend_cutoff;
    private Toggle[] toggle_df_type;

    private int lastSelectedImage = 0;

    public TextMeshPro debugUI;

    // if "generateDF" is true, script will instantly try to generate a DF (calling upon manager script to do this), then set this to false when complete.
    private bool generateDF = false;

    // these are references to cameras positioned to where the "zoom" views should be
    public GameObject canvas_zoomed_out;
    public GameObject canvas_zoomed_in;
    public GameObject camera_zoomed_out;
    public GameObject camera_zoomed_in_left;
    public GameObject camera_zoomed_in_right;



    [System.Serializable]
    public struct DropdownEntry
    {
        public Texture2D tex;
        public string displayName;
        public Color outlineColor;
    }
    public List<DropdownEntry> dropdownList;

    // camera move speed for when zoomed in
    public float cameraMoveSpeed = 1f;

    [Space]
    [Header("Automation Image Capture")]

    public Camera screenCapCameraLeft;
    public Camera screenCapCameraRight;
    public GameObject cameraCaptureQuad;

    [Space]

    public bool takeScreenCapture = false;
    public bool takeScreenCaptureLeft = false;
    public bool saveScreenCaptureInGenericFolder = false;

    [Space]

    public int autoRangeFirst = 0;
    public int autoRangeLast = 0;
    public int autoDFType = 0;
    public int autoDFSize = 0;
    public bool autoBatchTakeScreenCapture = false;
    public bool takeDFOutlineExport = false;
    public bool takeRegSDFExport = false;

    private void OnApplicationQuit()
    {
        gameStopped = true;
        Debug.Log(Time.time + " - Game is stopping now.");
        Resources.UnloadUnusedAssets();
    }

    public int CheckGameIsOver(string functionName)
    {
        if (gameStopped == true)
        {
            //Debug.Log(Time.time + " - " + functionName + " called, but game is stopping now, should just do nothing.");
            return -1;
        }
        else if (Time.time < 1.0)
        {
            //Debug.Log(Time.time + " - " + functionName + " called, but game time is < 1.0 (probably 0.0), meaning the game just started or just ended. Do nothing.");
            return -2;
        }
        else
        {
            //Debug.Log(Time.time + " - " + functionName + " called... why?");
            return 0;
        }
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        gameStopped = false;

        FindUIElements();
        InitializeDropdownInput();
        InitializeSetUIElements();

        ren_in.sharedMaterial.SetTexture("_MainTex", dropdownList[0].tex);

        SetDefaultMatValues();
        SetDefaultUIValues();

        camera_left_start_position = camera_zoomed_in_left.transform.position;
        camera_right_start_position = camera_zoomed_in_right.transform.position;

        debugUI.gameObject.SetActive(true);
    }

    // reset shader parameters each time you launch the scene
    private void SetDefaultMatValues()
    {
        ren_out.sharedMaterial.SetFloat("_DistanceCutoff", 0.5f);
        ren_out.sharedMaterial.SetFloat("_BlendCutoff", 0.2f);
        ren_out.sharedMaterial.SetFloat("_BlendGradCutoff", 0.0f);

        ren_out.sharedMaterial.SetFloat("_RenderOutline", 1.0f);
        ren_out.sharedMaterial.SetFloat("_RenderBackground", 1.0f);
        ren_out.sharedMaterial.SetFloat("_FilterMode", 1.0f);
        ren_out.sharedMaterial.SetFloat("_ColorSpaceMode", 0.0f);
        ren_out.sharedMaterial.SetFloat("_AntiAliasing", 0.0f);

        quad_mat_debug.SetActive(false);
    }

    // reset UI parameters (sliders) each time you launch the scene
    private void SetDefaultUIValues()
    {
        slider_outline_cutoff.value = ren_out.sharedMaterial.GetFloat("_DistanceCutoff");
        slider_region_cutoff.value = ren_out.sharedMaterial.GetFloat("_BlendCutoff");
        slider_blend_cutoff.value = ren_out.sharedMaterial.GetFloat("_BlendGradCutoff");

        if (ren_out.sharedMaterial.GetFloat("_RenderOutline") == 1.0f)
            toggle_render_outline.isOn = true;
        else
            toggle_render_outline.isOn = false;
        if (ren_out.sharedMaterial.GetFloat("_RenderBackground") == 1.0f)
            toggle_render_background.isOn = true;
        else
            toggle_render_background.isOn = false;
        if (ren_out.sharedMaterial.GetFloat("_FilterMode") == 1.0f)
            toggle_bilinear_filter.isOn = true;
        else
            toggle_bilinear_filter.isOn = false;
        if (ren_out.sharedMaterial.GetFloat("_ColorSpaceMode") == 1.0f)
            toggle_colorspace_sRGB.isOn = true;
        else
            toggle_colorspace_sRGB.isOn = false;
        if (ren_out.sharedMaterial.GetFloat("_AntiAliasing") == 1.0f)
            toggle_mat_aa.isOn = true;
        else
            toggle_mat_aa.isOn = false;

    }

    bool startedRemakingDF = false;
    void Update()
    {
        lastClick += Time.deltaTime;

        if (generateDF == true && lastClick > 0.1f)
        {
            generateDF = false;
            startedRemakingDF = true;
            float time1 = Time.realtimeSinceStartup;

            // remake the Distance Field file
            dfManager.RemakeDistanceField();
            
            float time2 = Time.realtimeSinceStartup;

            debugUI.text = "Finished, displaying DF.";
            debugUI.text += "\n Time to process (sec): " + (time2 - time1);

            startedRemakingDF = false;
        } else
        {
            ProcessInputCameraMovement();
        }
        if (startedRemakingDF == true)
        {
            debugUI.text = "Stopped calculating DF. Try again.";
            startedRemakingDF = false;
        }

        // below are some options related to auto-batch rendering distance fields, or saving screenshots of the renders to files. 
        if (takeScreenCapture == true)
        {
            takeScreenCapture = false;
            StartCoroutine(TakeScreencap(0));
        }
        if (takeScreenCaptureLeft == true)
        {
            takeScreenCaptureLeft = false;
            StartCoroutine(TakeScreencap(1));
        }
        if (autoBatchTakeScreenCapture == true)
        {
            autoBatchTakeScreenCapture = false;
            StartCoroutine(TakeScreencapBatch());
        }
        if (takeDFOutlineExport == true)
        {
            takeDFOutlineExport = false;
            StartCoroutine(TakeDFOutlineExport());
        }
        if (takeRegSDFExport == true)
        {
            takeRegSDFExport = false;
            StartCoroutine(TakeRegSDFExport());
        }

       
    }

    private void LateUpdate()
    {
        if (generateDF == true)
        {
            debugUI.text = "Processing DF...";
        }

    }

    // instead of manually setting EVERY UI element in the Unity inspector panel, we set them here by searching for them by name (the name of each UI element matters!)
    public void FindUIElements()
    {
        Transform g;

        g = ui_top_panel.transform.Find("Dropdown_SelectInput");
        if (g == null)
            Debug.Log("Object called Dropdown_SelectInput not found.");
        dropdown_select_input = g.GetComponent<TMP_Dropdown>();
        if (dropdown_select_input == null)
            Debug.Log("Dropdown called Dropdown_SelectInput not found.");

        toggle_list_dfsize = new Toggle[4];
        g = ui_top_panel.transform.Find("Toggle_DF_32");
        if (g == null)
            Debug.Log("Object called Toggle_DF_32 not found.");
        toggle_list_dfsize[0] = g.GetComponent<Toggle>();
        if (toggle_list_dfsize[0] == null)
            Debug.Log("Toggle called Toggle_DF_32 not found.");
        g = ui_top_panel.transform.Find("Toggle_DF_64");
        if (g == null)
            Debug.Log("Object called Toggle_DF_64 not found.");
        toggle_list_dfsize[1] = g.GetComponent<Toggle>();
        if (toggle_list_dfsize[1] == null)
            Debug.Log("Toggle called Toggle_DF_64 not found.");
        g = ui_top_panel.transform.Find("Toggle_DF_128");
        if (g == null)
            Debug.Log("Object called Toggle_DF_128 not found.");
        toggle_list_dfsize[2] = g.GetComponent<Toggle>();
        if (toggle_list_dfsize[2] == null)
            Debug.Log("Toggle called Toggle_DF_128 not found.");
        g = ui_top_panel.transform.Find("Toggle_DF_256");
        if (g == null)
            Debug.Log("Object called Toggle_DF_256 not found.");
        toggle_list_dfsize[3] = g.GetComponent<Toggle>();
        if (toggle_list_dfsize[3] == null)
            Debug.Log("Toggle called Toggle_DF_256 not found.");

        toggle_list_olcolor = new Toggle[2];
        g = ui_top_panel.transform.Find("Toggle_AutoOutlineColor");
        if (g == null)
            Debug.Log("Object called Toggle_AutoOutlineColor not found.");
        toggle_list_olcolor[0] = g.GetComponent<Toggle>();
        if (toggle_list_olcolor[0] == null)
            Debug.Log("Toggle called Toggle_AutoOutlineColor not found.");
        g = ui_top_panel.transform.Find("Toggle_ManualOutlineColor");
        if (g == null)
            Debug.Log("Object called Toggle_ManualOutlineColor not found.");
        toggle_list_olcolor[1] = g.GetComponent<Toggle>();
        if (toggle_list_olcolor[1] == null)
            Debug.Log("Toggle called Toggle_ManualOutlineColor not found.");

        g = ui_top_panel.transform.Find("Toggle_ProcessOutline");
        if (g == null)
            Debug.Log("Object called Toggle_ProcessOutline not found.");
        toggle_process_outline = g.GetComponent<Toggle>();
        if (toggle_process_outline == null)
            Debug.Log("Toggle called Toggle_ProcessOutline not found.");
        g = ui_top_panel.transform.Find("Toggle_ProcessBackground");
        if (g == null)
            Debug.Log("Object called Toggle_ProcessBackground not found.");
        toggle_process_background = g.GetComponent<Toggle>();
        if (toggle_process_background == null)
            Debug.Log("Toggle called Toggle_ProcessBackground not found.");

        g = ui_bottom_panel.transform.Find("Slider_OutlineCutoff");
        if (g == null)
            Debug.Log("Object called Slider_OutlineCutoff not found.");
        slider_outline_cutoff = g.GetComponent<Slider>();
        if (slider_outline_cutoff == null)
            Debug.Log("Toggle called Slider_OutlineCutoff not found.");
        g = ui_bottom_panel.transform.Find("Slider_RegionCutoff");
        if (g == null)
            Debug.Log("Object called Slider_RegionCutoff not found.");
        slider_region_cutoff = g.GetComponent<Slider>();
        if (slider_region_cutoff == null)
            Debug.Log("Toggle called Slider_RegionCutoff not found.");
        g = ui_bottom_panel.transform.Find("Slider_BlendCutoff");
        if (g == null)
            Debug.Log("Object called Slider_BlendCutoff not found.");
        slider_blend_cutoff = g.GetComponent<Slider>();
        if (slider_blend_cutoff == null)
            Debug.Log("Toggle called Slider_BlendCutoff not found.");

        g = ui_bottom_panel.transform.Find("Toggle_RenderOutline");
        if (g == null)
            Debug.Log("Object called Toggle_RenderOutline not found.");
        toggle_render_outline = g.GetComponent<Toggle>();
        if (toggle_render_outline == null)
            Debug.Log("Toggle called Toggle_RenderOutline not found.");
        g = ui_bottom_panel.transform.Find("Toggle_RenderBackground");
        if (g == null)
            Debug.Log("Object called Toggle_RenderBackground not found.");
        toggle_render_background = g.GetComponent<Toggle>();
        if (toggle_render_background == null)
            Debug.Log("Toggle called Toggle_RenderBackground not found.");
        g = ui_bottom_panel.transform.Find("Toggle_RenderBilinear");
        if (g == null)
            Debug.Log("Object called Toggle_RenderBilinear not found.");
        toggle_bilinear_filter = g.GetComponent<Toggle>();
        if (toggle_bilinear_filter == null)
            Debug.Log("Toggle called Toggle_RenderBilinear not found.");
        g = ui_bottom_panel.transform.Find("Toggle_ColorSpace_sRGB");
        if (g == null)
            Debug.Log("Object called Toggle_ColorSpace_sRGB not found.");
        toggle_colorspace_sRGB = g.GetComponent<Toggle>();
        if (toggle_colorspace_sRGB == null)
            Debug.Log("Toggle called Toggle_ColorSpace_sRGB not found.");

        toggle_df_type = new Toggle[3];
        g = ui_top_panel.transform.Find("Toggle_DF_Linear");
        if (g == null)
            Debug.Log("Object called Toggle_DF_Linear not found.");
        toggle_df_type[0] = g.GetComponent<Toggle>();
        if (toggle_df_type[0] == null)
            Debug.Log("Toggle called Toggle_DF_Linear not found.");
        g = ui_top_panel.transform.Find("Toggle_DF_SignedDF");
        if (g == null)
            Debug.Log("Object called Toggle_DF_SignedDF not found.");
        toggle_df_type[1] = g.GetComponent<Toggle>();
        if (toggle_df_type[1] == null)
            Debug.Log("Toggle called Toggle_DF_SignedDF not found.");
        g = ui_top_panel.transform.Find("Toggle_DF_Exponential");
        if (g == null)
            Debug.Log("Object called Toggle_DF_Exponential not found.");
        toggle_df_type[2] = g.GetComponent<Toggle>();
        if (toggle_df_type[2] == null)
            Debug.Log("Toggle called Toggle_DF_Exponential not found.");
        g = ui_bottom_panel.transform.Find("Toggle_AA");

        if (g == null)
            Debug.Log("Object called Toggle_AA not found.");
        toggle_mat_aa = g.GetComponent<Toggle>();
        if (toggle_mat_aa == null)
            Debug.Log("Toggle called Toggle_AA not found.");

        toggle_render_layers = new Toggle[4, 4];
        g = ui_bottom_panel.transform.Find("Toggle_NormalRender");
        toggle_render_layers[0, 0] = g.GetComponent<Toggle>();
        toggle_render_layers[0, 1] = g.GetComponent<Toggle>();
        g = ui_bottom_panel.transform.Find("Toggle_BackgroundRender");
        toggle_render_layers[0, 2] = g.GetComponent<Toggle>();
        toggle_render_layers[0, 3] = g.GetComponent<Toggle>();
        g = ui_bottom_panel.transform.Find("Toggle_A_R");
        toggle_render_layers[1, 0] = g.GetComponent<Toggle>();
        g = ui_bottom_panel.transform.Find("Toggle_A_G");
        toggle_render_layers[1, 1] = g.GetComponent<Toggle>();
        g = ui_bottom_panel.transform.Find("Toggle_A_B");
        toggle_render_layers[1, 2] = g.GetComponent<Toggle>();
        g = ui_bottom_panel.transform.Find("Toggle_A_A");
        toggle_render_layers[1, 3] = g.GetComponent<Toggle>();
        g = ui_bottom_panel.transform.Find("Toggle_B_R");
        toggle_render_layers[2, 0] = g.GetComponent<Toggle>();
        g = ui_bottom_panel.transform.Find("Toggle_B_G");
        toggle_render_layers[2, 1] = g.GetComponent<Toggle>();
        g = ui_bottom_panel.transform.Find("Toggle_B_B");
        toggle_render_layers[2, 2] = g.GetComponent<Toggle>();
        g = ui_bottom_panel.transform.Find("Toggle_B_A");
        toggle_render_layers[2, 3] = g.GetComponent<Toggle>();
        g = ui_bottom_panel.transform.Find("Toggle_C_R");
        toggle_render_layers[3, 0] = g.GetComponent<Toggle>();
        g = ui_bottom_panel.transform.Find("Toggle_C_G");
        toggle_render_layers[3, 1] = g.GetComponent<Toggle>();
        g = ui_bottom_panel.transform.Find("Toggle_C_B");
        toggle_render_layers[3, 2] = g.GetComponent<Toggle>();
        g = ui_bottom_panel.transform.Find("Toggle_C_A");
        toggle_render_layers[3, 3] = g.GetComponent<Toggle>();

        ren_out = quad_mat_out.GetComponent<Renderer>();
        ren_in = quad_mat_in.GetComponent<Renderer>();
        ren_debug = quad_mat_debug.GetComponent<Renderer>();
    }

    // initialize dropdown list of images from a data structure set up in the Unity Inspector panel
    public void InitializeDropdownInput()
    {
        if (dropdown_select_input == null)
        {
            Debug.Log("Can't initialize Dropdown, it wasn't found.");
            return;
        }

        dropdown_select_input.ClearOptions();

        List<string> dropdownOptions = new List<string>();
        //dropdownOptions.Add("Option 1");
        //dropdownOptions.Add("Option 2");
        for (int i = 0; i < dropdownList.Count; i++)
        {
            dropdownOptions.Add(i + " - " + dropdownList[i].displayName);
        }

        dropdown_select_input.AddOptions(dropdownOptions);
    }

    // initialize UI buttons / toggles to be on or off, every time the scene launches
    public void InitializeSetUIElements()
    {
        for (int i = 0; i < toggle_list_dfsize.Length; i++)
        {
            toggle_list_dfsize[i].isOn = false;
        }
        toggle_list_dfsize[1].isOn = true;

        for (int i = 0; i < toggle_list_olcolor.Length; i++)
        {
            toggle_list_olcolor[i].isOn = false;
        }
        toggle_list_olcolor[0].isOn = true;

        for (int i = 0; i < toggle_df_type.Length; i++)
        {
            toggle_df_type[i].isOn = false;
        }
        toggle_df_type[2].isOn = true;

        toggle_process_outline.isOn = true;
        toggle_process_background.isOn = true;
        toggle_colorspace_sRGB.isOn = false;
        toggle_mat_aa.isOn = false;

        int x = 0;
        int y = 0;
        for (int i = 0; i < toggle_render_layers.GetLength(0); i++)
        {
            for (int j = 0; j < toggle_render_layers.GetLength(1); j++)
            {
                toggle_render_layers[i, j].isOn = false;
            }
        }
        toggle_render_layers[x, y].isOn = true;
    }

    // when user clicks and changes Dropdown value...
    public void UpdateDropdownSelected(int choice)
    {
        if (CheckGameIsOver("UpdateDropdownSelected()") < 0)
            return;

        //Debug.Log("UpdateDropdownSelected() - choice changed - " + choice);
        Texture2D tex = dropdownList[choice].tex;

        lastSelectedImage = choice;

        Material mat = quad_mat_in.GetComponent<Renderer>().sharedMaterial;
        mat.SetTexture("_MainTex", tex);
    }

    public void UpdateOutlineSliderValueChanged(Single value)
    {
        if (CheckGameIsOver("UpdateDropdownSelected()") < 0)
            return;
        ren_out.sharedMaterial.SetFloat("_DistanceCutoff", value);
    }

    public void UpdateRegionSliderValueChanged(Single value)
    {
        if (CheckGameIsOver("UpdateDropdownSelected()") < 0)
            return;
        ren_out.sharedMaterial.SetFloat("_BlendCutoff", value);
    }

    public void UpdateBlendSliderValueChanged(Single value)
    {
        if (CheckGameIsOver("UpdateDropdownSelected()") < 0)
            return;
        ren_out.sharedMaterial.SetFloat("_BlendGradCutoff", value);
    }
    
    public void UpdateDistanceFieldSize(int choice)
    {
        if (CheckGameIsOver("UpdateDropdownSelected()") < 0)
            return;
        if (lastClick < 0.2f)
        {
            return;
        }
        lastClick = 0f;

        for (int i = 0; i < toggle_list_dfsize.Length; i++)
        {
            toggle_list_dfsize[i].isOn = false;
        }
        toggle_list_dfsize[choice].isOn = true;
    }

    public void UpdateOutlineColorChoice(int choice)
    {
        if (CheckGameIsOver("UpdateDropdownSelected()") < 0)
            return;
        if (lastClick < 0.2f)
        {
            return;
        }
        lastClick = 0f;

        for (int i = 0; i < toggle_list_olcolor.Length; i++)
        {
            toggle_list_olcolor[i].isOn = false;
        }
        toggle_list_olcolor[choice].isOn = true;
    }

    public void UpdateDistanceFieldType(int choice)
    {
        if (CheckGameIsOver("UpdateDropdownSelected()") < 0)
            return;
        if (lastClick < 0.2f)
        {
            return;
        }
        lastClick = 0f;

        for (int i = 0; i < toggle_df_type.Length; i++)
        {
            toggle_df_type[i].isOn = false;
        }
        toggle_df_type[choice].isOn = true;
    }

    public void UpdateRenderOutline(bool isOn)
    {
        if (CheckGameIsOver("UpdateDropdownSelected()") < 0)
            return;
        if (lastClick < 0.2f)
        {
            return;
        }
        lastClick = 0f;

        if (isOn == true)
        {
            ren_out.sharedMaterial.SetFloat("_RenderOutline", 1);
        }
        else
        {
            ren_out.sharedMaterial.SetFloat("_RenderOutline", 0);
        }
    }

    public void UpdateRenderBackground(bool isOn)
    {
        if (CheckGameIsOver("UpdateDropdownSelected()") < 0)
            return;
        if (lastClick < 0.2f)
        {
            return;
        }
        lastClick = 0f;

        if (isOn == true)
        {
            ren_out.sharedMaterial.SetFloat("_RenderBackground", 1);
        }
        else
        {
            ren_out.sharedMaterial.SetFloat("_RenderBackground", 0);
        }
    }

    public void UpdateColorSpaceSRGB(bool isOn)
    {
        if (CheckGameIsOver("UpdateDropdownSelected()") < 0)
            return;
        if (lastClick < 0.2f)
        {
            return;
        }
        lastClick = 0f;

        if (isOn == true)
        {
            ren_out.sharedMaterial.SetFloat("_ColorSpaceMode", 3);
        }
        else
        {
            ren_out.sharedMaterial.SetFloat("_ColorSpaceMode", 0);
        }
    }

    public void UpdateRenderBilinearFilter(bool isOn)
    {
        if (CheckGameIsOver("UpdateDropdownSelected()") < 0)
            return;
        if (lastClick < 0.2f)
        {
            return;
        }
        lastClick = 0f;

        if (isOn == true)
        {
            ren_out.sharedMaterial.SetFloat("_FilterMode", 1);
        } else
        {
            ren_out.sharedMaterial.SetFloat("_FilterMode", 0);
        }
    }

    public void UpdateDecrementInputImage()
    {
        if (CheckGameIsOver("UpdateDropdownSelected()") < 0)
            return;
        if (lastClick < 0.05f)
        {
            return;
        }
        lastClick = 0f;

        lastSelectedImage = Mathf.Max(0, lastSelectedImage - 1);
        dropdown_select_input.value = lastSelectedImage;
        int choice = lastSelectedImage;

        Texture2D tex = dropdownList[choice].tex;
        lastSelectedImage = choice;
        Material mat = quad_mat_in.GetComponent<Renderer>().sharedMaterial;
        mat.SetTexture("_MainTex", tex);
    }

    public void UpdateIncrementInputImage()
    {
        if (CheckGameIsOver("UpdateDropdownSelected()") < 0)
            return;
        if (lastClick < 0.05f)
        {
            return;
        }
        lastClick = 0f;

        lastSelectedImage = Mathf.Min(dropdownList.Count - 1, lastSelectedImage + 1);
        dropdown_select_input.value = lastSelectedImage;
        int choice = lastSelectedImage;

        Texture2D tex = dropdownList[choice].tex;
        lastSelectedImage = choice;
        Material mat = quad_mat_in.GetComponent<Renderer>().sharedMaterial;
        mat.SetTexture("_MainTex", tex);
    }

    public void UpdateRenderLayer(int layerInt)
    {
        if (CheckGameIsOver("UpdateRenderLayer()") < 0)
            return;
        if (lastClick < 0.2f)
        {
            return;
        }
        lastClick = 0f;

        int y = Mathf.FloorToInt(layerInt / 10);
        int x = layerInt % 10;
        Debug.Log("UpdateRenderLayer() - x = " + x + ", y = " + y);
        for (int i = 0; i < toggle_render_layers.GetLength(0); i++)
        {
            for (int j = 0; j < toggle_render_layers.GetLength(1); j++)
            {
                toggle_render_layers[i, j].isOn = false;
            }
        }
        toggle_render_layers[y, x].isOn = true;

        if (x == 0 && y == 0)
        {
            quad_mat_debug.gameObject.SetActive(false);
            quad_mat_out.gameObject.SetActive(true);
        } else
        {
            quad_mat_debug.gameObject.SetActive(true);
            quad_mat_out.gameObject.SetActive(false);

            Texture2D tex = new Texture2D(dfManager.df_size, dfManager.df_size);
            if (y == 0 && x == 2)
            {
                tex = ren_out.sharedMaterial.GetTexture("_BackgroundTex") as Texture2D;
            } else if (y == 1)
            {
                if (x == 0)
                {
                    Texture2D tempTex = ren_out.sharedMaterial.GetTexture("_DistanceFieldTex") as Texture2D;
                    for (int i = 0; i < tempTex.width; i++)
                    {
                        for (int j = 0; j < tempTex.height; j++)
                        {
                            tex.SetPixel(i, j, new Color(tempTex.GetPixel(i, j).r, 0, 0, 1));
                        }
                    }
                } else if (x == 1)
                {
                    Texture2D tempTex = ren_out.sharedMaterial.GetTexture("_DistanceFieldTex") as Texture2D;
                    for (int i = 0; i < tempTex.width; i++)
                    {
                        for (int j = 0; j < tempTex.height; j++)
                        {
                            tex.SetPixel(i, j, new Color(0, tempTex.GetPixel(i, j).g, 0, 1));
                        }
                    }
                } else if (x == 2)
                {
                    Texture2D tempTex = ren_out.sharedMaterial.GetTexture("_DistanceFieldTex") as Texture2D;
                    for (int i = 0; i < tempTex.width; i++)
                    {
                        for (int j = 0; j < tempTex.height; j++)
                        {
                            tex.SetPixel(i, j, new Color(0, 0, tempTex.GetPixel(i, j).b, 1));
                        }
                    }
                }
                else if (x == 3)
                {
                    Texture2D tempTex = ren_out.sharedMaterial.GetTexture("_DistanceFieldTex") as Texture2D;
                    for (int i = 0; i < tempTex.width; i++)
                    {
                        for (int j = 0; j < tempTex.height; j++)
                        {
                            tex.SetPixel(i, j, new Color(tempTex.GetPixel(i, j).a, tempTex.GetPixel(i, j).a, tempTex.GetPixel(i, j).a, 1));
                        }
                    }
                }
            } else if (y == 2)
            {
                if (x == 0)
                {
                    Texture2D tempTex = ren_out.sharedMaterial.GetTexture("_DFTexBlend") as Texture2D;
                    for (int i = 0; i < tempTex.width; i++)
                    {
                        for (int j = 0; j < tempTex.height; j++)
                        {
                            tex.SetPixel(i, j, new Color(tempTex.GetPixel(i, j).r, 0, 0, 1));
                        }
                    }
                }
                else if (x == 1)
                {
                    Texture2D tempTex = ren_out.sharedMaterial.GetTexture("_DFTexBlend") as Texture2D;
                    for (int i = 0; i < tempTex.width; i++)
                    {
                        for (int j = 0; j < tempTex.height; j++)
                        {
                            tex.SetPixel(i, j, new Color(0, tempTex.GetPixel(i, j).g, 0, 1));
                        }
                    }
                }
                else if (x == 2)
                {
                    Texture2D tempTex = ren_out.sharedMaterial.GetTexture("_DFTexBlend") as Texture2D;
                    for (int i = 0; i < tempTex.width; i++)
                    {
                        for (int j = 0; j < tempTex.height; j++)
                        {
                            tex.SetPixel(i, j, new Color(0, 0, tempTex.GetPixel(i, j).b, 1));
                        }
                    }
                }
                else if (x == 3)
                {
                    Texture2D tempTex = ren_out.sharedMaterial.GetTexture("_DFTexBlend") as Texture2D;
                    for (int i = 0; i < tempTex.width; i++)
                    {
                        for (int j = 0; j < tempTex.height; j++)
                        {
                            tex.SetPixel(i, j, new Color(tempTex.GetPixel(i, j).a, tempTex.GetPixel(i, j).a, tempTex.GetPixel(i, j).a, 1));
                        }
                    }
                }
            } else if (y == 3) {
                if (x == 0)
                {
                    Texture2D tempTex = ren_out.sharedMaterial.GetTexture("_DFTexBlendMap") as Texture2D;
                    for (int i = 0; i < tempTex.width; i++)
                    {
                        for (int j = 0; j < tempTex.height; j++)
                        {
                            tex.SetPixel(i, j, new Color(tempTex.GetPixel(i, j).r*10f, 0, 0, 1));
                        }
                    }
                }
                else if (x == 1)
                {
                    Texture2D tempTex = ren_out.sharedMaterial.GetTexture("_DFTexBlendMap") as Texture2D;
                    for (int i = 0; i < tempTex.width; i++)
                    {
                        for (int j = 0; j < tempTex.height; j++)
                        {
                            tex.SetPixel(i, j, new Color(0, tempTex.GetPixel(i, j).g*10f, 0, 1));
                        }
                    }
                }
                else if (x == 2)
                {
                    Texture2D tempTex = ren_out.sharedMaterial.GetTexture("_DFTexBlendMap") as Texture2D;
                    for (int i = 0; i < tempTex.width; i++)
                    {
                        for (int j = 0; j < tempTex.height; j++)
                        {
                            tex.SetPixel(i, j, new Color(0, 0, tempTex.GetPixel(i, j).b*10f, 1));
                        }
                    }
                }
                else if (x == 3)
                {
                    Texture2D tempTex = ren_out.sharedMaterial.GetTexture("_DFTexBlendMap") as Texture2D;
                    for (int i = 0; i < tempTex.width; i++)
                    {
                        for (int j = 0; j < tempTex.height; j++)
                        {
                            tex.SetPixel(i, j, new Color(tempTex.GetPixel(i, j).a * 10f, tempTex.GetPixel(i, j).a * 10f, tempTex.GetPixel(i, j).a * 10f, 1));
                        }
                    }
                }
            }
            tex.filterMode = FilterMode.Point;
            tex.Apply();
            ren_debug.sharedMaterial.SetTexture("_MainTex", tex);
        }
    }

    // user clicked the big button to generate a DF with applied settings...
    public void GenerateDF()
    {
        if (CheckGameIsOver("GenerateDF()") < 0)
            return;
        if (lastClick < 0.2f)
        {
            return;
        }
        lastClick = 0f;

        for (int i = 0; i < toggle_list_dfsize.Length; i++)
        {
            if (toggle_list_dfsize[i].isOn == true)
            {
                if (i == 0)
                {
                    dfManager.df_size = 512;
                } else if (i == 1)
                {
                    dfManager.df_size = 64;
                } else if (i == 2)
                {
                    dfManager.df_size = 128;
                } else if (i == 3)
                {
                    dfManager.df_size = 256;
                }
            }
        }

        for (int i = 0; i < toggle_df_type.Length; i++)
        {
            if (toggle_df_type[i].isOn == true)
            {
                dfManager.df_type = i;
            }
        }

        dfManager.manualOutlineColor = dropdownList[lastSelectedImage].outlineColor;
        if (toggle_list_olcolor[0].isOn == true)
        {
            dfManager.autoOutlineColor = true;
        } else
        {
            dfManager.autoOutlineColor = false;
        }

        dfManager.processOutline = toggle_process_outline.isOn;
        dfManager.processBackground = toggle_process_background.isOn;

        generateDF = true;
        //dfManager.RemakeDistanceField();
    }

    // Bypass waiting for the next frame to generate DF 
    public void GenerateDFNow()
    {
        /*if (CheckGameIsOver("GenerateDF()") < 0)
            return;
        if (lastClick < 0.2f)
        {
            return;
        }
        lastClick = 0f;*/

        for (int i = 0; i < toggle_list_dfsize.Length; i++)
        {
            if (toggle_list_dfsize[i].isOn == true)
            {
                if (i == 0)
                {
                    dfManager.df_size = 512;
                }
                else if (i == 1)
                {
                    dfManager.df_size = 64;
                }
                else if (i == 2)
                {
                    dfManager.df_size = 128;
                }
                else if (i == 3)
                {
                    dfManager.df_size = 256;
                }
            }
        }

        for (int i = 0; i < toggle_df_type.Length; i++)
        {
            if (toggle_df_type[i].isOn == true)
            {
                dfManager.df_type = i;
            }
        }

        dfManager.manualOutlineColor = dropdownList[lastSelectedImage].outlineColor;
        if (toggle_list_olcolor[0].isOn == true)
        {
            dfManager.autoOutlineColor = true;
        }
        else
        {
            dfManager.autoOutlineColor = false;
        }

        dfManager.processOutline = toggle_process_outline.isOn;
        dfManager.processBackground = toggle_process_background.isOn;

        //generateDF = true;
        dfManager.RemakeDistanceField();
    }

    public void UpdateZoomIn()
    {
        if (CheckGameIsOver("UpdateDropdownSelected()") < 0)
            return;
        if (lastClick < 0.05f)
        {
            return;
        }
        lastClick = 0f;

        canvas_zoomed_in.SetActive(true);
        camera_zoomed_in_left.GetComponent<Camera>().enabled = true;
        camera_zoomed_in_right.GetComponent<Camera>().enabled = true;

        canvas_zoomed_out.SetActive(false);
        camera_zoomed_out.GetComponent<Camera>().enabled = false;
    }

    public void UpdateZoomOut()
    {
        if (CheckGameIsOver("UpdateDropdownSelected()") < 0)
            return;
        if (lastClick < 0.05f)
        {
            return;
        }
        lastClick = 0f;

        canvas_zoomed_in.SetActive(false);
        camera_zoomed_in_left.GetComponent<Camera>().enabled = false;
        camera_zoomed_in_right.GetComponent<Camera>().enabled = false;

        canvas_zoomed_out.SetActive(true);
        camera_zoomed_out.GetComponent<Camera>().enabled = true;
    }

    
    public void ProcessInputCameraMovement()
    {
        float moveX = Input.GetAxis("Horizontal");
        float moveY = Input.GetAxis("Vertical");

        moveX = moveX * Time.deltaTime * cameraMoveSpeed;
        moveY = moveY * Time.deltaTime * cameraMoveSpeed;

        camera_zoomed_in_left.transform.Translate(moveX, moveY, 0f);
        camera_zoomed_in_right.transform.Translate(moveX, moveY, 0f);
    }

    private Vector3 camera_left_start_position;
    private Vector3 camera_right_start_position;
    public void UpdateResetCameraPosition()
    {
        camera_zoomed_in_left.transform.position = camera_left_start_position;
        camera_zoomed_in_right.transform.position = camera_right_start_position;
    }

    public void UpdateShaderAA(bool isOn)
    {
        if (CheckGameIsOver("UpdateDropdownSelected()") < 0)
            return;
        if (lastClick < 0.05f)
        {
            return;
        }
        lastClick = 0f;

        Material mat = quad_mat_out.GetComponent<Renderer>().sharedMaterial;
        float aa = 0f;
        if (isOn == true)
            aa = 1f;
        mat.SetFloat("_AntiAliasing", aa);
    }

    public int exportWidth = 512;
    public int exportHeight = 512;
    public System.Collections.IEnumerator TakeScreencap(int whichSide)
    {
        //https://discussions.unity.com/t/how-to-save-manually-save-a-png-of-a-camera-view/683911

        // Cameras already in place. If want to render full size, set FOV = 45. To render zoomed in (as little as 32x32 pixels captured), set FOV = 3.

        yield return new WaitForEndOfFrame();

        //RenderTexture currentRT = RenderTexture.active;
        camera_zoomed_out.GetComponent<Camera>().enabled = false;
        camera_zoomed_in_left.GetComponent<Camera>().enabled = false;
        camera_zoomed_in_right.GetComponent<Camera>().enabled = false;
        if (whichSide == 0)
        {
            screenCapCameraRight.enabled = true;
        }
        if (whichSide == 1)
        {
            screenCapCameraLeft.enabled = true;
        }

        //RenderTexture currentRT = RenderTexture.active;
        //RenderTexture.active = screenCapCameraRight.targetTexture;

        yield return new WaitForEndOfFrame();


        //RenderTexture screenCapRight = screenCapCameraRight.targetTexture as RenderTexture;
        if (whichSide == 0)
        {
            screenCapCameraRight.Render();
        }
        if (whichSide == 1)
        {
            screenCapCameraLeft.Render();
        }
        yield return new WaitForEndOfFrame();

        int width = exportWidth;
        int height = exportHeight;
        Texture2D imageRight = new Texture2D(width, height);
        //imageRight.ReadPixels(new Rect(0, 0, width, height), (int)(screenCapCameraRight.pixelWidth*0.5f), 0);
        // above doesn't work at all... but RenderTexture did in fact render out to a quad correctly, maybe we can just write directly to it...
        //Texture2D cameraCap = cameraCaptureQuad.GetComponent<Renderer>().sharedMaterial.GetTexture("_MainTex") as Texture2D;
        RenderTexture.active = cameraCaptureQuad.GetComponent<Renderer>().sharedMaterial.GetTexture("_MainTex") as RenderTexture;
        RenderTexture cameraCap = cameraCaptureQuad.GetComponent<Renderer>().sharedMaterial.GetTexture("_MainTex") as RenderTexture;
        imageRight.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        imageRight.Apply();
        //RenderTexture.active = currentRT;

        var Bytes = imageRight.EncodeToPNG();
        string filePath = Application.dataPath + "\\Resources\\DebugOut\\";
        DateTime now = DateTime.Now;
        if (saveScreenCaptureInGenericFolder == false)
        {
            filePath += "" + now.Year + "" + now.Month.ToString("D2") + "" + now.Day.ToString("D2") + "_" + now.Hour.ToString("D2") + "" + now.Minute.ToString("D2") + "" + now.Second.ToString("D2") + "\\";
        } else
        {
            filePath += "" + "DebugOutGeneral" + "\\";
        }
        if (Directory.Exists(filePath) == false)
        {
            Directory.CreateDirectory(filePath);
        }
        File.WriteAllBytes(filePath + "testout_001_" + Time.realtimeSinceStartup + ".png", Bytes);
        Destroy(imageRight);

        Debug.Log("Screenshot taken.");

        yield return new WaitForEndOfFrame();
        camera_zoomed_out.GetComponent<Camera>().enabled = true;
        camera_zoomed_in_left.GetComponent<Camera>().enabled = false;
        camera_zoomed_in_right.GetComponent<Camera>().enabled = false;
        if (whichSide == 0)
        {
            screenCapCameraRight.enabled = false;
        } else if (whichSide == 1)
        {
            screenCapCameraLeft.enabled = false;
        }
    }

    public System.Collections.IEnumerator TakeScreencapBatch()
    {
        float time1 = Time.realtimeSinceStartup;
        Debug.Log("Starting TakeScreencapBatch()...");

        yield return new WaitForEndOfFrame();
        string filePath = Application.dataPath + "\\Resources\\DebugOut\\";
        DateTime now = DateTime.Now;
        filePath += "" + now.Year + "" + now.Month.ToString("D2") + "" + now.Day.ToString("D2") + "_" + now.Hour.ToString("D2") + "" + now.Minute.ToString("D2") + "" + now.Second.ToString("D2") + "\\";
        if (Directory.Exists(filePath) == false)
        {
            Directory.CreateDirectory(filePath);
        }
        camera_zoomed_out.GetComponent<Camera>().enabled = false;
        camera_zoomed_in_left.GetComponent<Camera>().enabled = false;
        camera_zoomed_in_right.GetComponent<Camera>().enabled = false;


        for (int i = autoRangeFirst; i < autoRangeLast; i++)
        {
            lastSelectedImage = i;
            if (lastSelectedImage < 0 || lastSelectedImage > dropdownList.Count - 1)
            {
                Debug.Log("Batch image at " + i + " is out of range, ending early.");
                break;
            }
            /*dropdown_select_input.value = lastSelectedImage;
            Texture2D tex = dropdownList[lastSelectedImage].tex;
            Material mat = quad_mat_in.GetComponent<Renderer>().sharedMaterial;
            mat.SetTexture("_MainTex", tex);*/
            Debug.Log("Batch capture image " + i + " of " + autoRangeLast + " ...");
            UpdateDropdownSelected(lastSelectedImage);
            UpdateDistanceFieldSize(autoDFSize);
            UpdateDistanceFieldType(autoDFType);
            yield return new WaitForEndOfFrame();
            try { 
                GenerateDFNow();
            } catch
            {
                Debug.Log("(some error occurred, but keep going...)");
            }
            //dfManager.RemakeDistanceField();
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            Debug.Log("Finished DF...");

            int width = 512;
            int height = 512;

            //screenCapCameraRight.enabled = true;
            screenCapCameraLeft.enabled = true;
            screenCapCameraRight.enabled = false;
            // Mathf.Max(0, lastSelectedImage - 1);

            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            //screenCapCameraRight.Render();
            screenCapCameraLeft.Render();
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();

            Texture2D imageOut = new Texture2D(width, height);
            RenderTexture.active = cameraCaptureQuad.GetComponent<Renderer>().sharedMaterial.GetTexture("_MainTex") as RenderTexture;
            RenderTexture cameraCap = cameraCaptureQuad.GetComponent<Renderer>().sharedMaterial.GetTexture("_MainTex") as RenderTexture;
            imageOut.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            imageOut.Apply();
            yield return new WaitForEndOfFrame();

            var Bytes = imageOut.EncodeToPNG();
            File.WriteAllBytes(filePath + "img_" + i.ToString("D3") + "_aa_og" + ".png", Bytes);
            Destroy(imageOut);
            yield return new WaitForEndOfFrame();

            screenCapCameraLeft.enabled = false;
            screenCapCameraRight.enabled = true;

            yield return new WaitForEndOfFrame();
            //screenCapCameraRight.Render();
            screenCapCameraRight.Render();
            yield return new WaitForEndOfFrame();

            imageOut = new Texture2D(width, height);
            RenderTexture.active = cameraCaptureQuad.GetComponent<Renderer>().sharedMaterial.GetTexture("_MainTex") as RenderTexture;
            cameraCap = cameraCaptureQuad.GetComponent<Renderer>().sharedMaterial.GetTexture("_MainTex") as RenderTexture;
            imageOut.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            imageOut.Apply();
            yield return new WaitForEndOfFrame();

            Bytes = imageOut.EncodeToPNG();
            string dfTypeName = "blank";
            string dfSizeName = "blank";
            if (autoDFType == 2)
            {
                dfTypeName = "relsdf";
            } else if (autoDFType == 1)
            {
                dfTypeName = "sdf";
            } else if (autoDFType == 0)
            {
                dfTypeName = "df";
            }
            if (autoDFSize == 0)
            {
                dfSizeName = "512";
            }
            else if (autoDFSize == 3)
            {
                dfSizeName = "256";
            } else if (autoDFSize == 2)
            {
                dfSizeName = "128";
            } else if (autoDFSize == 1)
            {
                dfSizeName = "64";
            }
            File.WriteAllBytes(filePath + "img_" + i.ToString("D3") + "_" + dfTypeName + "_" + dfSizeName + ".png", Bytes);
            Destroy(imageOut);
            yield return new WaitForEndOfFrame();

        }

        yield return new WaitForEndOfFrame();
        camera_zoomed_out.GetComponent<Camera>().enabled = true;
        camera_zoomed_in_left.GetComponent<Camera>().enabled = false;
        camera_zoomed_in_right.GetComponent<Camera>().enabled = false;
        screenCapCameraRight.enabled = false;
        screenCapCameraLeft.enabled = false;

        float time2 = Time.realtimeSinceStartup;
        Debug.Log("Finished TakeScreenshotBatch()... time to complete was: " + (time2 - time1));
    }


    public System.Collections.IEnumerator TakeDFOutlineExport()
    {
        yield return new WaitForEndOfFrame();

        Texture2D ogDf = (Texture2D)ren_out.sharedMaterial.GetTexture("_DistanceFieldTex");
        int width = ogDf.width;
        int height = ogDf.height;
        Texture2D imageRight = new Texture2D(width, height, TextureFormat.R8, false, true);

        for (int i = 0; i < width; i++)
        {
            for (int j = 0; j < height; j++)
            {
                imageRight.SetPixel(i, j, ogDf.GetPixel(i, j));
            }
        }
        imageRight.Apply();

        var Bytes = imageRight.EncodeToPNG();
        string filePath = Application.dataPath + "\\Resources\\DebugOut\\";
        DateTime now = DateTime.Now;
        if (saveScreenCaptureInGenericFolder == false)
        {
            filePath += "" + now.Year + "" + now.Month.ToString("D2") + "" + now.Day.ToString("D2") + "_" + now.Hour.ToString("D2") + "" + now.Minute.ToString("D2") + "" + now.Second.ToString("D2") + "\\";
        }
        else
        {
            filePath += "" + "DebugOutGeneral" + "\\";
        }
        if (Directory.Exists(filePath) == false)
        {
            Directory.CreateDirectory(filePath);
        }
        File.WriteAllBytes(filePath + "testout_001_" + Time.realtimeSinceStartup + ".png", Bytes);
        Destroy(imageRight);

        Debug.Log("Printed out DF to image file.");

    }

    public System.Collections.IEnumerator TakeRegSDFExport()
    {
        yield return new WaitForEndOfFrame();

        Texture2D ogDf = (Texture2D)ren_out.sharedMaterial.GetTexture("_DFTexBlend");
        int width = ogDf.width;
        int height = ogDf.height;
        Texture2D imageRight = new Texture2D(width, height, TextureFormat.RGBA32, false, true);

        for (int i = 0; i < width; i++)
        {
            for (int j = 0; j < height; j++)
            {
                imageRight.SetPixel(i, j, ogDf.GetPixel(i, j));
            }
        }
        imageRight.Apply();

        var Bytes = imageRight.EncodeToPNG();
        string filePath = Application.dataPath + "\\Resources\\DebugOut\\";
        DateTime now = DateTime.Now;
        if (saveScreenCaptureInGenericFolder == false)
        {
            filePath += "" + now.Year + "" + now.Month.ToString("D2") + "" + now.Day.ToString("D2") + "_" + now.Hour.ToString("D2") + "" + now.Minute.ToString("D2") + "" + now.Second.ToString("D2") + "\\";
        }
        else
        {
            filePath += "" + "DebugOutGeneral" + "\\";
        }
        if (Directory.Exists(filePath) == false)
        {
            Directory.CreateDirectory(filePath);
        }
        File.WriteAllBytes(filePath + "testout_001_" + Time.realtimeSinceStartup + ".png", Bytes);
        Destroy(imageRight);

        Debug.Log("Printed out RegDF (4 channels only) to image file.");

    }
}
