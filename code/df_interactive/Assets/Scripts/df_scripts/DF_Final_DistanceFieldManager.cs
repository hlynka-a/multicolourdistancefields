using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

/*
    DF_Final_DistanceFieldManager.cs
    * This manages creating a Distance Field (either DF, SDF, or RelSDF + RegSDF) and setting them to be rendered.
 
 */

public class DF_Final_DistanceFieldManager : MonoBehaviour
{
    // input and output objects to get or set texture / sprite
    public GameObject quad_mat_in;
    public GameObject quad_mat_out;
    private Renderer ren_in;
    private Renderer ren_out;

    // df_size = resolution of the field
    public int df_size = 64;
    // df_type = 0 means DF, 1 = SDF, 2 = RelSDF + RegSDF
    public int df_type = 1;
    // autoOutlineColor = if true, try to guess the single outline color of the cartoon image using a naive heuristic, if false, use the pre-set outline color defined in the list of images (saved to manualOutlineColor)
    public bool autoOutlineColor = true;
    public Color manualOutlineColor = Color.black;

    // processOutline = generate RelSDF?
    public bool processOutline = true;
    // processBackground = generate RegSDF?
    public bool processBackground = true;
    // 0 = use nearest skeletonized point, 1 = interpolate between 2 nearest skeletonized point (meant to simulate generating DF from a vector asset instead of a raster asset, theoretically more accurate, but difference was minor in our testing)
    public int rasterInterpMode = 0;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        ren_out = quad_mat_out.GetComponent<Renderer>();
        ren_in = quad_mat_in.GetComponent<Renderer>();
    }

    float[,,] ConvertTextureToFloat(Texture2D tex)
    {
        float[,,] returnArray = new float[tex.width, tex.height, 4];
        for (int i = 0; i < tex.width; i++)
        {
            for (int j = 0; j < tex.height; j++)
            {
                returnArray[i, j, 0] = tex.GetPixel(i, j).r;
                returnArray[i, j, 1] = tex.GetPixel(i, j).g;
                returnArray[i, j, 2] = tex.GetPixel(i, j).b;
                returnArray[i, j, 3] = tex.GetPixel(i, j).a;
            }
        }
        return returnArray;
    }

    Texture2D ConvertFloatToTexture(float[,,] array)
    {
        Texture2D returnTex = new Texture2D(array.GetLength(0), array.GetLength(1));

        for (int i = 0; i < returnTex.width; i++)
        {
            for (int j = 0; j < returnTex.height; j++)
            {
                returnTex.SetPixel(i, j, new Color(array[i, j, 0], array[i, j, 1], array[i, j, 2], array[i, j, 3]));
            }
        }
        returnTex.Apply();
        returnTex.filterMode = FilterMode.Point;

        return returnTex;
    }

    /* Pipeline of RemakeDistanceField():
        * flatten colors in input image
        * find outline color of input image
        * extract outline from background, fill in gaps in background with neighboring colors
        * calculate Distance Field (RelSDF) for outline
        * calculate Distance Field (RegSDF) for background
        * set results to the shader to render
     */
    public void RemakeDistanceField()
    {
        string debugOut = "Total time debug analysis (time passed since start):\n";
        float time1 = Time.realtimeSinceStartup;
        float time2 = Time.realtimeSinceStartup;

        // remake Distance Field, set to outputPreviewMat
        ren_out.sharedMaterial.SetTexture("_MainTex", ren_in.sharedMaterial.GetTexture("_MainTex"));

        Material obMat = quad_mat_out.GetComponent<Renderer>().sharedMaterial;
        Texture2D ogTex = obMat.GetTexture("_MainTex") as Texture2D;
        float[,,] ogTexArray = ConvertTextureToFloat(ogTex);

        // FlattenColors3() - combination of FlattenColors() "color quantization by pruning OcTree" and FlattenColors2() "histogram"
        ogTexArray = FlattenColors3(ogTexArray);

        time2 = Time.realtimeSinceStartup;
        debugOut += "* FlattenColors3 - (sec) " + (time2 - time1) + "\n";

        //ogTex = ConvertFloatToTexture(ogTexArray);

        Color outlineColor = manualOutlineColor;
        float colorTolerance = obMat.GetFloat("_ColorTolerance");
        if (autoOutlineColor == true)
        {
            //outlineColor = GetOutlineColor(ogTex, colorTolerance);
            outlineColor = GetOutlineColor(ogTexArray, colorTolerance);
            time2 = Time.realtimeSinceStartup;
            debugOut += "* GetOutlineColor - (sec) " + (time2 - time1) + "\n";
        }

        // naive method by intuition to further flatten colors
        // issue: this will cause thin outlines to disapppear sometimes - needs to handle outlines carefully...
        ogTexArray = DownsampleToReduceColors(ogTexArray, outlineColor);

        time2 = Time.realtimeSinceStartup;
        debugOut += "* DownsampleToReduceColors - (sec) " + (time2 - time1) + "\n";
        //ogTex = ConvertFloatToTexture(ogTexArray);

        Texture2D bgTex;
        float[,,] bgTexArray = new float[ogTexArray.GetLength(0), ogTexArray.GetLength(1), 4];
        if (processOutline == true)
        {
            // 0.9f extreme case for textures with alpha - outline.colorTolerance = 0.5f works much better 
            // colorTolerance = 0.5f causes some regions to completely disappear, not part of region or outline. Try 0.2f or smaller.
            // DON'T hardcode a value: everything should be based on "colorTolerance" value from shader. This helps ensure pixel will either be in outline or background, not neither.
            bgTexArray = RemoveOutlineColor(ogTexArray, outlineColor, colorTolerance + 0.05f);
            time2 = Time.realtimeSinceStartup;
            debugOut += "* RemoveOutlineColor - (sec) " + (time2 - time1) + "\n";
        }
        else
        {
            bgTexArray = ogTexArray;
        }
        //bgTex.filterMode = FilterMode.Point;
        //ogTex = ConvertFloatToTexture(ogTexArray);
        //bgTex = ConvertFloatToTexture(bgTexArray);

        Texture2D dfTex = new Texture2D(ogTex.width, ogTex.height);
        float[,,] dfTexArray = new float[ogTexArray.GetLength(0), ogTexArray.GetLength(1), 4];
        if (processOutline == true)
        {
            dfTexArray = CalculateDistanceField(ogTexArray, bgTexArray, outlineColor, colorTolerance);
            time2 = Time.realtimeSinceStartup;
            debugOut += "* CalculateDistanceField (outline) - (sec) " + (time2 - time1) + "\n";
        }
        //dfTex.filterMode = FilterMode.Point;
        dfTex = ConvertFloatToTexture(dfTexArray);
        bgTex = ConvertFloatToTexture(bgTexArray);
        time2 = Time.realtimeSinceStartup;
        debugOut += "* ConvertingFloatToTexture twice - (sec) " + (time2 - time1) + "\n";
        Texture2D dfTexBlend = new Texture2D(bgTex.width, bgTex.height, TextureFormat.RGBA32, false, true);
        Texture2D dfTexBlend2 = new Texture2D(bgTex.width, bgTex.height, TextureFormat.RGBA32, false, true);
        if (processBackground == true)
        {
            dfTexBlend = CalculateDistanceFieldInteriorColor(ogTex, bgTex, outlineColor, colorTolerance);
            time2 = Time.realtimeSinceStartup;
            debugOut += "* CalculateDistanceFieldInteriorColor - (sec) " + (time2 - time1) + "\n";
            float[,] dfTexBlendA = GetColorChannel(dfTexBlend, 4);
            for (int i = 0; i < dfTexBlendA.GetLength(0); i++)
            {
                for (int j = 0; j < dfTexBlendA.GetLength(1); j++)
                {
                    dfTexBlend2.SetPixel(i, j, new Color(dfTexBlendA[i, j], 0, 0, 1));
                }
            }
            dfTexBlend2.Apply();
        }
        dfTexBlend.filterMode = FilterMode.Point;
        dfTexBlend2.filterMode = FilterMode.Point;

        dfTex = ReduceTextureResolution(dfTex, 0, df_size);
        bgTex = ReduceTextureResolution(bgTex, 0, df_size);
        dfTexBlend = ReduceTextureResolution(dfTexBlend, 2, df_size);
        dfTexBlend2 = ReduceTextureResolution(dfTexBlend2, 2, df_size);
        dfTex.filterMode = FilterMode.Point;
        bgTex.filterMode = FilterMode.Point;
        dfTexBlend.filterMode = FilterMode.Point;
        time2 = Time.realtimeSinceStartup;
        debugOut += "* ReduceTextureResolution - (sec) " + (time2 - time1) + "\n";

        dfTex = Calculate4ColorMappingLutToTexture(dfTex, bgTex, dfTexBlend);
        time2 = Time.realtimeSinceStartup;
        debugOut += "* Calculate4ColorMappingLutToTexture - (sec) " + (time2 - time1) + "\n";
        Texture2D dfTexBlendMap = new Texture2D(bgTex.width, bgTex.height, TextureFormat.RGBA32, false, true);
        if (processBackground == true)
        {
            dfTexBlendMap = Calculate4ColorMappingToTexture(dfTex, bgTex, dfTexBlend);
            time2 = Time.realtimeSinceStartup;
            debugOut += "* Calculate4ColorMappingToTexture (RegSDF) - (sec) " + (time2 - time1) + "\n";
        }
        dfTexBlendMap.filterMode = FilterMode.Point;
        dfTex.filterMode = FilterMode.Point;

        // R8 is better for color representation without converting from linear to not... but R8_SIGNED returns same expected values as RGBA32 does... sometimes...
        Texture2D sc_dfTex = new Texture2D(dfTex.width, dfTex.height, TextureFormat.R8_SIGNED, false, true);
        Texture2D sc_dfTexLUT = new Texture2D(dfTex.width, dfTex.height, TextureFormat.R8, false, false);
        Texture2D sc_dfBlendR = new Texture2D(dfTexBlend.width, dfTexBlend.height, TextureFormat.R8_SIGNED, false, true);
        Texture2D sc_dfBlendG = new Texture2D(dfTexBlend.width, dfTexBlend.height, TextureFormat.R8_SIGNED, false, true);
        Texture2D sc_dfBlendB = new Texture2D(dfTexBlend.width, dfTexBlend.height, TextureFormat.R8_SIGNED, false, true);
        Texture2D sc_dfBlendA = new Texture2D(dfTexBlend.width, dfTexBlend.height, TextureFormat.R8_SIGNED, false, true);
        Texture2D sc_dfBlendMapR = new Texture2D(dfTexBlendMap.width, dfTexBlendMap.height, TextureFormat.R8, false, true);
        Texture2D sc_dfBlendMapG = new Texture2D(dfTexBlendMap.width, dfTexBlendMap.height, TextureFormat.R8, false, true);
        Texture2D sc_dfBlendMapB = new Texture2D(dfTexBlendMap.width, dfTexBlendMap.height, TextureFormat.R8, false, true);
        Texture2D sc_dfBlendMapA = new Texture2D(dfTexBlendMap.width, dfTexBlendMap.height, TextureFormat.R8, false, true);

        for (int i = 0; i < sc_dfTex.width; i++)
        {
            for (int j = 0; j < sc_dfTex.height; j++)
            {
                sc_dfTex.SetPixel(i, j, new Color(dfTex.GetPixel(i, j).r, 0, 0, 1));
                sc_dfTexLUT.SetPixel(i, j, new Color(dfTex.GetPixel(i, j).g, 0, 0, 1));
                sc_dfBlendR.SetPixel(i, j, new Color(dfTexBlend.GetPixel(i, j).r, 0, 0, 1));
                sc_dfBlendG.SetPixel(i, j, new Color(dfTexBlend.GetPixel(i, j).g, 0, 0, 1));
                sc_dfBlendB.SetPixel(i, j, new Color(dfTexBlend.GetPixel(i, j).b, 0, 0, 1));
                sc_dfBlendA.SetPixel(i, j, new Color(dfTexBlend.GetPixel(i, j).a, 0, 0, 1));
                sc_dfBlendMapR.SetPixel(i, j, new Color(dfTexBlendMap.GetPixel(i, j).r, 0, 0, 1));
                sc_dfBlendMapG.SetPixel(i, j, new Color(dfTexBlendMap.GetPixel(i, j).g, 0, 0, 1));
                sc_dfBlendMapB.SetPixel(i, j, new Color(dfTexBlendMap.GetPixel(i, j).b, 0, 0, 1));
                sc_dfBlendMapA.SetPixel(i, j, new Color(dfTexBlendMap.GetPixel(i, j).a, 0, 0, 1));
            }
        }
        sc_dfTex.Apply();
        sc_dfTexLUT.Apply();
        sc_dfBlendR.Apply();
        sc_dfBlendG.Apply();
        sc_dfBlendB.Apply();
        sc_dfBlendA.Apply();
        sc_dfBlendMapR.Apply();
        sc_dfBlendMapG.Apply();
        sc_dfBlendMapB.Apply();
        sc_dfBlendMapA.Apply();
        sc_dfTex.filterMode = FilterMode.Point;
        sc_dfTexLUT.filterMode = FilterMode.Point;
        sc_dfBlendR.filterMode = FilterMode.Point;
        sc_dfBlendG.filterMode = FilterMode.Point;
        sc_dfBlendB.filterMode = FilterMode.Point;
        sc_dfBlendA.filterMode = FilterMode.Point;
        sc_dfBlendMapR.filterMode = FilterMode.Point;
        sc_dfBlendMapG.filterMode = FilterMode.Point;
        sc_dfBlendMapB.filterMode = FilterMode.Point;
        sc_dfBlendMapA.filterMode = FilterMode.Point;

        obMat.SetTexture("_DistanceFieldTex", dfTex);
        obMat.SetTexture("_BackgroundTex", bgTex);
        obMat.SetTexture("_DFTexBlend", dfTexBlend);
        obMat.SetTexture("_DFTexBlend2", dfTexBlend2);
        obMat.SetTexture("_DFTexBlendMap", dfTexBlendMap);
        obMat.SetColor("_Color", outlineColor);

        obMat.SetTexture("_SC_DFTex", sc_dfTex);
        obMat.SetTexture("_SC_DFTexLUT", sc_dfTexLUT);
        obMat.SetTexture("_SC_DFTexBlendR", sc_dfBlendR);
        obMat.SetTexture("_SC_DFTexBlendG", sc_dfBlendG);
        obMat.SetTexture("_SC_DFTexBlendB", sc_dfBlendB);
        obMat.SetTexture("_SC_DFTexBlendA", sc_dfBlendA);
        obMat.SetTexture("_SC_DFTexBlendMapR", sc_dfBlendMapR);
        obMat.SetTexture("_SC_DFTexBlendMapG", sc_dfBlendMapG);
        obMat.SetTexture("_SC_DFTexBlendMapB", sc_dfBlendMapB);
        obMat.SetTexture("_SC_DFTexBlendMapA", sc_dfBlendMapA);

        time2 = Time.realtimeSinceStartup;
        debugOut += "* assign to textures, finish - (sec) " + (time2 - time1) + "\n";
        Debug.Log(debugOut);
    }

    private Texture2D Calculate4ColorMappingToTexture(Texture2D ogTex, Texture2D bgTex, Texture2D dfTexBlend)
    {
        /*
                LUT already calculated... now need to creating mapping to correct index color in LUT. 
         */
        float time1 = Time.realtimeSinceStartup;

        Texture2D returnTex = new Texture2D(ogTex.width, ogTex.height, TextureFormat.RGBA32, false, true);

        float[,] dfR = Calculate4ColorMapToLut(bgTex, dfTexBlend, GetColorChannel(dfTexBlend, 1), GetColorChannel(ogTex, 2));
        float[,] dfG = Calculate4ColorMapToLut(bgTex, dfTexBlend, GetColorChannel(dfTexBlend, 2), GetColorChannel(ogTex, 2));
        float[,] dfB = Calculate4ColorMapToLut(bgTex, dfTexBlend, GetColorChannel(dfTexBlend, 3), GetColorChannel(ogTex, 2));
        float[,] dfA = Calculate4ColorMapToLut(bgTex, dfTexBlend, GetColorChannel(dfTexBlend, 4), GetColorChannel(ogTex, 2));
        for (int x = 0; x < ogTex.width; x++)
        {
            for (int y = 0; y < ogTex.height; y++)
            {
                returnTex.SetPixel(x, y, new Color(dfR[x, y] / 255.0f, dfG[x, y] / 255.0f, dfB[x, y] / 255.0f, dfA[x, y] / 255.0f));
            }
        }
        returnTex.Apply();

        return returnTex;
    }

    private float[,] Calculate4ColorMapToLut(Texture2D ogTex, Texture2D colorMapTex, float[,] colorMapTexChannel, float[,] colorLut)
    {
        float[,] returnLayer = new float[ogTex.width, ogTex.height];

        for (int x = 0; x < ogTex.width; x++)
        {
            for (int y = 0; y < ogTex.height; y++)
            {
                returnLayer[x, y] = -1f;
            }
        }

        List<Color> listOfColors = new List<Color>();
        listOfColors = ExtractUniqueColorsFromLut(colorLut);

        // get peaks in the channel
        List<Vector2> channelPeaks = new List<Vector2>();
        for (int x = 0; x <= ogTex.width - 1; x++)
        {
            for (int y = 0; y <= ogTex.height - 1; y++)
            {
                Vector2Int point = new Vector2Int(x, y);
                Vector2[] pNeigh = new Vector2[4];
                pNeigh[0] = point + new Vector2(1, 0);
                pNeigh[1] = point + new Vector2(-1, 0);
                pNeigh[2] = point + new Vector2(0, 1);
                pNeigh[3] = point + new Vector2(0, -1);
                bool isPeak = true;
                for (int j = 0; j < pNeigh.Length; j++)
                {
                    int x1 = (int)pNeigh[j].x; int y1 = (int)pNeigh[j].y;
                    if (x1 < 0 || x1 >= ogTex.width || y1 < 0 || y1 >= ogTex.height)
                    {
                        continue;
                    }
                    if (colorMapTexChannel[x, y] > 0 && colorMapTexChannel[x, y] >= colorMapTexChannel[(int)pNeigh[j].x, (int)pNeigh[j].y])
                    {

                    }
                    else
                    {
                        isPeak = false;
                    }
                }
                if (isPeak == true)
                {
                    channelPeaks.Add(new Vector2(x, y));
                }
            }
        }

        List<Vector2> listOfPoints = new List<Vector2>();
        for (int i = 0; i < channelPeaks.Count; i++)
        {
            Color newColor = ogTex.GetPixel((int)channelPeaks[i].x, (int)channelPeaks[i].y);
            for (int j = 0; j < listOfColors.Count; j++)
            {
                if (ColorEquals(newColor, listOfColors[j]) == true)
                {
                    returnLayer[(int)channelPeaks[i].x, (int)channelPeaks[i].y] = (float)(j);
                    listOfPoints.Add(channelPeaks[i]);
                    break;
                }
            }
        }
        int count = 0;
        while (listOfPoints.Count > 0 && count < ogTex.width * ogTex.height)
        {
            count++;

            int listIndex = listOfPoints.Count - 1;
            for (int i = listIndex; i >= 0; i--)
            {
                Vector2 point = listOfPoints[i];
                listOfPoints.RemoveAt(i);
                Vector2[] pNeigh = new Vector2[4];
                pNeigh[0] = point + new Vector2(1, 0);
                pNeigh[1] = point + new Vector2(-1, 0);
                pNeigh[2] = point + new Vector2(0, 1);
                pNeigh[3] = point + new Vector2(0, -1);
                for (int j = 0; j < pNeigh.Length; j++)
                {
                    int x1 = (int)pNeigh[j].x; int y1 = (int)pNeigh[j].y;
                    if (x1 < 0 || x1 >= ogTex.width || y1 < 0 || y1 >= ogTex.height)
                    {
                        continue;
                    }
                    if (returnLayer[x1, y1] == -1f)
                    {
                        if (colorMapTexChannel[x1, y1] <= colorMapTexChannel[(int)point.x, (int)point.y])
                        {
                            returnLayer[x1, y1] = returnLayer[(int)point.x, (int)point.y];
                            listOfPoints.Add(pNeigh[j]);
                        }
                    }
                    else
                    {

                    }

                }
            }
        }
        int countRemaining = 0;
        for (int i = 0; i < returnLayer.GetLength(0); i++)
        {
            for (int j = 0; j < returnLayer.GetLength(1); j++)
            {
                if (returnLayer[i, j] == -1f)
                {
                    countRemaining++;
                }
            }
        }

        return returnLayer;
    }

    private List<Color> ExtractUniqueColorsFromLut(float[,] colorLut)
    {
        List<Color> listOfColors = new List<Color>();

        int width = colorLut.GetLength(0);
        int height = colorLut.GetLength(1);

        for (int y = 0; y < Mathf.FloorToInt(height * 0.25f); y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color newColor = new Color
                    (colorLut[x, y],
                    colorLut[x, y + Mathf.FloorToInt(height * 0.25f)],
                    colorLut[x, y + Mathf.FloorToInt(height * 0.5f)],
                    colorLut[x, y + Mathf.FloorToInt(height * 0.75f)]);
                if (newColor.a >= 0.1f)
                {
                    listOfColors.Add(newColor);
                }
                else if (newColor.a < 0.1f)
                {
                    listOfColors.Add(newColor);
                }
            }
        }

        return listOfColors;
    }

    //private Texture2D FlattenColors(Texture2D ogTex)
    private float[,,] FlattenColors(float[,,] ogTex)
    {
        /*
            Flatten Colors: "Color Quantization"
            Goal: to group distinct colors and reduce into distinct colors required to approximate image. Also used in vectorizing raster images.
            "octree" color quantization: use tree datastructure with at most 8 children per branch - a way of grouping similar colors together.

            https://en.wikipedia.org/wiki/Color_quantization
            Javascript code: https://observablehq.com/@tmcw/octree-color-quantization
            Python code: https://github.com/delimitry/octree_color_quantizer , https://delimitry.blogspot.com/2016/02/octree-color-quantizer-in-python.html 

            Note: "functions within functions" used here, to match design of code to Javascript example above.

            Results: If FlattenColors palette size "numOfColors" is too small to be practical, system will return empty palette (size = 0).
            I could start with a small number (e.g. "8"), check the size of the palette, and increase (*2) until the palette size is > 0.
            Algorithm is fast, takes < 1.00 seconds to run each time to try. 
        */
        //Texture2D returnTex = new Texture2D(ogTex.width, ogTex.height);
        float[,,] returnTex = new float[ogTex.GetLength(0), ogTex.GetLength(1), 4];

        HashSet<Color> uniqueC = new HashSet<Color>();
        for (int i = 0; i < ogTex.GetLength(0); i++)
        {
            for (int j = 0; j < ogTex.GetLength(1); j++)
            {
                Color tempC = new Color(ogTex[i, j, 0], ogTex[i, j, 1], ogTex[i, j, 2]);
                uniqueC.Add(tempC);
            }
        }

        int numOfColors = 8;
        List<Color> smallPalette = new List<Color>();
        Quantizer q = new Quantizer();
        while (smallPalette.Count <= 0 && numOfColors <= 256)
        {
            smallPalette.Clear();
            q = new Quantizer();
            foreach (Color c in uniqueC)
            {
                q.AddColor(c);
            }
            smallPalette = q.MakePalette(numOfColors);

            if (smallPalette.Count == 0)
            {
                // error occurred in making palette, size limit needs to increase to properly represent image
                numOfColors = numOfColors + 4;
            }
        }

        for (int i = 0; i < ogTex.GetLength(0); i++)
        {
            for (int j = 0; j < ogTex.GetLength(1); j++)
            {
                int index = q.GetPaletteIndex(new Color(ogTex[i, j, 0], ogTex[i, j, 1], ogTex[i, j, 2]));//.GetPixel(i, j));
                if (ogTex[i, j, 0] > 0.99f && ogTex[i, j, 1] > 0.99f && ogTex[i, j, 2] > 0.99f)
                {

                }
                Color newC;
                if (index == -1)
                {
                    newC = new Color(0.8f, 0.0f, 0.8f);
                }
                else
                {
                    newC = smallPalette[index];
                }
                if (ogTex[i, j, 3] < 0.1)
                {
                    newC.a = 0.0f;
                }
                else
                {
                    newC.a = 1.0f;
                }
                returnTex[i, j, 0] = newC.r;// (i, j, newC);
                returnTex[i, j, 1] = newC.g;
                returnTex[i, j, 2] = newC.b;
            }
        }
        //returnTex.Apply();

        return returnTex;
    }

    public class SortFreqColor
    {
        public Color c;
        public int f;

        public static int SortThis(SortFreqColor n1, SortFreqColor n2)
        {
            return n2.f - n1.f;
        }
    }

    //private Texture2D FlattenColors2(Texture2D ogTex)
    private float[,,] FlattenColors2(float[,,] ogTex)
    {
        /*  Naive histogram method: if a color occurs too infrequently, cull it, unless the remaining colors aren't close enough to all original colors on screen.
            Works quite well, except some regions (rarely) disappear or aren't represented if there's a gradient. 
         */
        float[,,] returnTex = new float[ogTex.GetLength(0), ogTex.GetLength(1), 4];

        Dictionary<Color, int> uniqueC = new Dictionary<Color, int>();
        for (int i = 0; i < ogTex.GetLength(0); i++)
        {
            for (int j = 0; j < ogTex.GetLength(1); j++)
            {
                Color tempC = new Color(ogTex[i, j, 0], ogTex[i, j, 1], ogTex[i, j, 2], ogTex[i, j, 3]);
                if (uniqueC.ContainsKey(tempC))
                {
                    uniqueC[tempC] = uniqueC[tempC] + 1;
                }
                else
                {
                    uniqueC.Add(tempC, 1);
                }
            }
        }
        SortFreqColor[] listOfColors = new SortFreqColor[uniqueC.Count];
        int kCount = 0;
        foreach (Color c in uniqueC.Keys)
        {
            SortFreqColor newColor = new SortFreqColor();
            newColor.c = c;
            newColor.f = uniqueC[c];
            listOfColors[kCount] = newColor;
            kCount++;
        }
        System.Array.Sort(listOfColors, SortFreqColor.SortThis);

        /*  What to do after this? Not clear there is a standard rule of thumb to cut out small frequency colors. 
            (should look to literature for more formal process)
            For now, personal heuristic: 
                * clamp out frequencies below (width * 0.05)*(height * 0.05).
                * test all pixels against all remaining colors, check that there are no color distances greater than some threshold. (say, total distance > 0.2f)
                * if there is a pixel greater than that distance, then reduce clamp cutoff to 0.5x, and repeat.
         */
        float diffCutoff = 1.5f;      // = 2f is large, but any value less causes the maximum iterations...
        int clampFreq = (int)((ogTex.GetLength(0) * 0.04f) * (ogTex.GetLength(1) * 0.04f));
        int maxClampCounter = 0;
        bool finishedColorClamp = true;
        List<Color> finalColors = new List<Color>();
        int[,] indexToNearestColor = new int[ogTex.GetLength(0), ogTex.GetLength(1)];
        do
        {
            finalColors = new List<Color>();
            for (int i = 0; i < listOfColors.Length; i++)
            {
                if (listOfColors[i].f > clampFreq)
                {
                    finalColors.Add(listOfColors[i].c);
                }
                else
                {
                    break;
                }
            }
            finishedColorClamp = true;
            for (int i = 0; i < ogTex.GetLength(0); i++)
            {
                for (int j = 0; j < ogTex.GetLength(1); j++)
                {
                    float minTotalDiff = 4f;
                    Color c2 = new Color(ogTex[i, j, 0], ogTex[i, j, 1], ogTex[i, j, 2], ogTex[i, j, 3]);
                    for (int k = 0; k < finalColors.Count; k++)
                    {
                        Color c1 = finalColors[k];
                        float totalDiff = Mathf.Abs(c1.r - c2.r) + Mathf.Abs(c1.g - c2.g) + Mathf.Abs(c1.b - c2.b) + Mathf.Abs(c1.a - c2.a);
                        if (totalDiff < minTotalDiff)
                        {
                            minTotalDiff = totalDiff;
                            indexToNearestColor[i, j] = k;
                        }
                    }
                    if (minTotalDiff > diffCutoff)
                    {
                        finishedColorClamp = false;
                        break;
                    }
                }
                if (finishedColorClamp == false)
                    break;
            }
            if (finishedColorClamp == false)
            {
                clampFreq = (int)(clampFreq * 0.5f);
                maxClampCounter++;
            }
        } while (finishedColorClamp == false && maxClampCounter < 5);

        for (int i = 0; i < ogTex.GetLength(0); i++)
        {
            for (int j = 0; j < ogTex.GetLength(1); j++)
            {
                returnTex[i, j, 0] = finalColors[indexToNearestColor[i, j]].r;
                returnTex[i, j, 1] = finalColors[indexToNearestColor[i, j]].g;
                returnTex[i, j, 2] = finalColors[indexToNearestColor[i, j]].b;
                returnTex[i, j, 3] = finalColors[indexToNearestColor[i, j]].a;
            }
        }
        //returnTex.Apply();

        return returnTex;
    }

    //private Texture2D FlattenColors3(Texture2D ogTex)
    private float[,,] FlattenColors3(float[,,] ogTex)
    {
        /*  Reasoning: FlattenColors() worked good for defining regions, but 1) not making similar colors into distinct regions properly, and 2) not perfectly matching the original color.
            Solution: Combine FlattenColors() and FlattenColors2() - compare against the original ogTex, and keep 1 of 2 results per pixel that is closest to the original.
         */

        //Texture2D returnTex = new Texture2D(ogTex.width, ogTex.height);
        //Texture2D reTex1 = FlattenColors(ogTex);
        //Texture2D reTex2 = FlattenColors2(ogTex);
        float[,,] returnTex = new float[ogTex.GetLength(0), ogTex.GetLength(1), 4];
        float[,,] reTex1 = FlattenColors(ogTex);
        float[,,] reTex2 = FlattenColors2(ogTex);

        for (int i = 0; i < ogTex.GetLength(0); i++)
        {
            for (int j = 0; j < ogTex.GetLength(1); j++)
            {
                Color cOG = new Color(ogTex[i, j, 0], ogTex[i, j, 1], ogTex[i, j, 2], ogTex[i, j, 3]);
                Color c1 = new Color(reTex1[i, j, 0], reTex1[i, j, 1], reTex1[i, j, 2], reTex1[i, j, 3]);
                Color c2 = new Color(reTex2[i, j, 0], reTex2[i, j, 1], reTex2[i, j, 2], reTex2[i, j, 3]);
                float cDiff1 = Mathf.Abs(cOG.r - c1.r) + Mathf.Abs(cOG.g - c1.g) + Mathf.Abs(cOG.b - c1.b) + Mathf.Abs(cOG.a - c1.a);
                float cDiff2 = Mathf.Abs(cOG.r - c2.r) + Mathf.Abs(cOG.g - c2.g) + Mathf.Abs(cOG.b - c2.b) + Mathf.Abs(cOG.a - c2.a);
                if (cDiff1 < cDiff2)
                {
                    returnTex[i, j, 0] = c1.r;
                    returnTex[i, j, 1] = c1.g;
                    returnTex[i, j, 2] = c1.b;
                    returnTex[i, j, 3] = c1.a;
                    //returnTex.SetPixel(i, j, c1);
                }
                else
                {
                    returnTex[i, j, 0] = c2.r;
                    returnTex[i, j, 1] = c2.g;
                    returnTex[i, j, 2] = c2.b;
                    returnTex[i, j, 3] = c2.a;
                    //returnTex.SetPixel(i, j, c2);
                }
            }
        }
        //returnTex.Apply();

        return returnTex;
    }

    //private Color GetOutlineColor(Texture2D tex, float tolerance)
    private Color GetOutlineColor(float[,,] tex, float tolerance)
    {
        Color returnColor = Color.black;

        int numOfChecks = tex.GetLength(1) * 2;
        Color[] outlineColors = new Color[tex.GetLength(1) * 2];
        int[] outlineColorCount = new int[tex.GetLength(1) * 2];
        for (int i = 0; i < outlineColorCount.Length; i++)
        {
            outlineColorCount[i] = 0;
        }
        for (int i = 0; i < tex.GetLength(1); i++)
        {
            Color color1 = new Color(0, 0, 0, 0);
            for (int x = 0; x < tex.GetLength(0); x++)
            {
                Color testColor = new Color(tex[x, i, 0], tex[x, i, 1], tex[x, i, 2], tex[x, i, 3]);//tex.GetPixel(x, i);
                if (testColor.a > 0)
                {
                    color1 = testColor;
                    break;
                }
            }
            outlineColors[i * 2] = color1;
            if (outlineColors[i * 2].a == 0)
            {
                outlineColorCount[i * 2] = 0;
            }
            else
            {
                int count = 0;
                for (int j = 0; j < i * 2; j++)
                {
                    Color c1 = outlineColors[j];
                    Color c2 = outlineColors[i * 2];
                    if (Vector3.Distance(
                        new Vector3(c1.r, c1.g, c1.b),
                        new Vector3(c2.r, c2.g, c2.b))
                        < tolerance)
                    {
                        count++;
                    }
                }
                outlineColorCount[i * 2] = count;
            }

            Color color2 = new Color(0, 0, 0, 0);
            for (int x = tex.GetLength(1) - 1; x >= 0; x--)
            {
                Color testColor = new Color(tex[x, i, 0], tex[x, i, 1], tex[x, i, 2], tex[x, i, 3]);
                if (testColor.a > 0)
                {
                    color2 = testColor;
                    break;
                }
            }
            outlineColors[(i * 2) + 1] = color1;
            if (outlineColors[(i * 2) + 1].a == 0)
            {
                outlineColorCount[(i * 2) + 1] = 0;
            }
            else
            {
                int count = 0;
                for (int j = 0; j < (i * 2) + 1; j++)
                {
                    Color c1 = outlineColors[j];
                    Color c2 = outlineColors[(i * 2) + 1];
                    if (Vector3.Distance(
                        new Vector3(c1.r, c1.g, c1.b),
                        new Vector3(c2.r, c2.g, c2.b))
                        < tolerance)
                    {
                        count++;
                    }
                }
                outlineColorCount[(i * 2) + 1] = count;
            }
        }

        int maxColor = 0;
        int maxColorIndex = 0;
        for (int i = 0; i < outlineColorCount.Length; i++)
        {
            if (outlineColorCount[i] >= maxColor)
            {
                maxColor = outlineColorCount[i];
                maxColorIndex = i;
            }
        }
        returnColor = new Color(outlineColors[maxColorIndex].r, outlineColors[maxColorIndex].g, outlineColors[maxColorIndex].b);

        return returnColor;
    }

    //private Texture2D DownsampleToReduceColors(Texture2D ogTex, Color outlineColor)
    private float[,,] DownsampleToReduceColors(float[,,] ogTex, Color outlineColor)
    {
        /*
            Description: 
                * Used because "FlattenColors" still results in weird outline artifacts.
                * Here, we downsample large texture to 64x64 texture (only works if input texture is higher than 64x64).
                    * Take most common color in neighbourhood, use in downsampled texture.
                * Then in original resolution image, compare color to immediate corresponding neighbourhood (3x3) of 64x64 texture, choose color that is closest to original color. 
                * Note: tends to remove thin details, including outlines. Need to add logic to handle outlines carefully.
         */

        //Texture2D returnTex = new Texture2D(ogTex.GetLength(0), ogTex.GetLength(1));
        //Texture2D lowTex = new Texture2D(64, 64);
        float[,,] returnTex = new float[ogTex.GetLength(0), ogTex.GetLength(1), 4];
        float[,,] lowTex = new float[64, 64, 4];
        List<Color>[,] lowTexColors = new List<Color>[64, 64];
        List<int>[,] lowTexColorCount = new List<int>[64, 64];

        for (int i = 0; i < lowTex.GetLength(0); i++)
        {
            for (int j = 0; j < lowTex.GetLength(1); j++)
            {
                lowTexColors[i, j] = new List<Color>();
                lowTexColorCount[i, j] = new List<int>();
            }
        }

        for (int i = 0; i < ogTex.GetLength(0); i++)
        {
            for (int j = 0; j < ogTex.GetLength(1); j++)
            {
                int x = (i / (ogTex.GetLength(0) / lowTex.GetLength(0)));
                int y = (j / (ogTex.GetLength(1) / lowTex.GetLength(1)));
                bool dupColor = false;
                for (int k = 0; k < lowTexColors[x, y].Count; k++)
                {
                    if (ColorEquals(lowTexColors[x, y][k], new float[] { ogTex[i, j, 0], ogTex[i, j, 1], ogTex[i, j, 2], ogTex[i, j, 3] }) == true)
                    {
                        lowTexColorCount[x, y][k]++;
                        dupColor = true;
                        break;
                    }
                }
                if (dupColor == false)
                {
                    lowTexColors[x, y].Add(new Color(ogTex[i, j, 0], ogTex[i, j, 1], ogTex[i, j, 2], ogTex[i, j, 3]));// ogTex.GetPixel(i, j));
                    lowTexColorCount[x, y].Add(1);
                }
            }
        }

        for (int i = 0; i < lowTex.GetLength(0); i++)
        {
            for (int j = 0; j < lowTex.GetLength(1); j++)
            {
                int highestCount = 0;
                int highestIndex = 0;
                for (int k = 0; k < lowTexColorCount[i, j].Count; k++)
                {
                    if (lowTexColorCount[i, j][k] > highestCount)
                    {
                        highestCount = lowTexColorCount[i, j][k];
                        highestIndex = k;
                    }
                }
                //lowTex.SetPixel(i, j, lowTexColors[i, j][highestIndex]);
                lowTex[i, j, 0] = lowTexColors[i, j][highestIndex].r;
                lowTex[i, j, 1] = lowTexColors[i, j][highestIndex].g;
                lowTex[i, j, 2] = lowTexColors[i, j][highestIndex].b;
                lowTex[i, j, 3] = lowTexColors[i, j][highestIndex].a;
            }
        }
        //lowTex.Apply();

        for (int i = 0; i < ogTex.GetLength(0); i++)
        {
            for (int j = 0; j < ogTex.GetLength(1); j++)
            {
                int x = (i / (ogTex.GetLength(0) / lowTex.GetLength(0)));
                int y = (j / (ogTex.GetLength(1) / lowTex.GetLength(1)));
                int xSelect = x;
                int ySelect = y;
                float colorDiff = 4.0f;
                Color ogColor = new Color(ogTex[i, j, 0], ogTex[i, j, 1], ogTex[i, j, 2], ogTex[i, j, 3]);//ogTex.GetPixel(i, j);
                int neighSize = 2;
                if (ColorEquals(outlineColor, new float[] { ogTex[i, j, 0], ogTex[i, j, 1], ogTex[i, j, 2], ogTex[i, j, 3] }) == false)
                {
                    for (int m = Mathf.Max(0, x - neighSize); m < Mathf.Min(lowTex.GetLength(0), x + neighSize); m++)
                    {
                        for (int n = Mathf.Max(0, y - neighSize); n < Mathf.Min(lowTex.GetLength(1), y + neighSize); n++)
                        {
                            Color tempColor = new Color(lowTex[m, n, 0], lowTex[m, n, 1], lowTex[m, n, 2], lowTex[m, n, 3]);//lowTex.GetPixel(m, n);
                            float tempDiff = Mathf.Abs(ogColor.r - tempColor.r) + Mathf.Abs(ogColor.g - tempColor.g) + Mathf.Abs(ogColor.b - tempColor.b) + Mathf.Abs(ogColor.a - tempColor.a);
                            if (tempDiff <= colorDiff)
                            {
                                colorDiff = tempDiff;
                                xSelect = m;
                                ySelect = n;
                            }
                        }
                    }
                    //returnTex.SetPixel(i, j, lowTex.GetPixel(xSelect, ySelect));
                    returnTex[i, j, 0] = lowTex[xSelect, ySelect, 0];
                    returnTex[i, j, 1] = lowTex[xSelect, ySelect, 1];
                    returnTex[i, j, 2] = lowTex[xSelect, ySelect, 2];
                    returnTex[i, j, 3] = lowTex[xSelect, ySelect, 3];
                }
                else
                {
                    //returnTex.SetPixel(i, j, ogTex.GetPixel(i, j));
                    returnTex[i, j, 0] = ogTex[i, j, 0];
                    returnTex[i, j, 1] = ogTex[i, j, 1];
                    returnTex[i, j, 2] = ogTex[i, j, 2];
                    returnTex[i, j, 3] = ogTex[i, j, 3];
                }
            }
        }
        //returnTex.Apply();

        return returnTex;
    }

    //private Texture2D RemoveOutlineColor(Texture2D ogTex, Color outlineColor, float tolerance)
    private float[,,] RemoveOutlineColor(float[,,] ogTex, Color outlineColor, float tolerance)
    {
        //Texture2D returnTex = new Texture2D(ogTex.GetLength(0), ogTex.GetLength(1));
        //Texture2D tempTex = new Texture2D(ogTex.GetLength(0), ogTex.GetLength(1));
        float[,,] returnTex = new float[ogTex.GetLength(0), ogTex.GetLength(1), 4];
        Color[,] returnTex2 = new Color[ogTex.GetLength(0), ogTex.GetLength(1)];
        Color[,] tempTex = new Color[ogTex.GetLength(0), ogTex.GetLength(1)];
        //ogTex.CopyTo(returnTex, 0);
        //ogTex.CopyTo(tempTex, 0);
        for (int i = 0; i < ogTex.GetLength(0); i++)
        {
            for (int j = 0; j < ogTex.GetLength(1); j++)
            {
                returnTex2[i, j] = new Color(ogTex[i, j, 0], ogTex[i, j, 1], ogTex[i, j, 2], ogTex[i, j, 3]);
                tempTex[i, j] = new Color(ogTex[i, j, 0], ogTex[i, j, 1], ogTex[i, j, 2], ogTex[i, j, 3]);
            }
        }
        bool stillOutlineLeft = true;
        while (stillOutlineLeft == true)
        {
            stillOutlineLeft = false;
            for (int i = 1; i < returnTex2.GetLength(0) - 1; i++)
            {
                for (int j = 1; j < returnTex2.GetLength(1) - 1; j++)
                {
                    Color c1 = returnTex2[i, j];// returnTex.GetPixel(i, j);
                    Vector3 vc1 = new Vector3(c1.r, c1.g, c1.b);
                    Vector3 vc2 = new Vector3(outlineColor.r, outlineColor.g, outlineColor.b);
                    if (Vector3.Distance(vc1, vc2) < tolerance && c1.a >= 0.1f)
                    {
                        stillOutlineLeft = true;
                        Color cN = returnTex2[i, j - 1];//.GetPixel(i, j - 1);
                        Color cS = returnTex2[i, j + 1];//returnTex.GetPixel(i, j + 1);
                        Color cE = returnTex2[i + 1, j];//returnTex.GetPixel(i + 1, j);
                        Color cW = returnTex2[i - 1, j];//returnTex.GetPixel(i - 1, j);
                        Vector3 vcN = new Vector3(cN.r, cN.g, cN.b);
                        Vector3 vcS = new Vector3(cS.r, cS.g, cS.b);
                        Vector3 vcE = new Vector3(cE.r, cE.g, cE.b);
                        Vector3 vcW = new Vector3(cW.r, cW.g, cW.b);
                        if (Vector3.Distance(vc2, vcN) >= tolerance || cN.a < 0.1f)
                        {
                            tempTex[i, j] = cN;//tempTex.SetPixel(i, j, cN);
                        }
                        else if (Vector3.Distance(vc2, vcS) >= tolerance || cS.a < 0.1f)
                        {
                            tempTex[i, j] = cS;//tempTex.SetPixel(i, j, cS);
                        }
                        else if (Vector3.Distance(vc2, vcE) >= tolerance || cE.a < 0.1f)
                        {
                            tempTex[i, j] = cE;//tempTex.SetPixel(i, j, cE);
                        }
                        else if (Vector3.Distance(vc2, vcW) >= tolerance || cW.a < 0.1f)
                        {
                            tempTex[i, j] = cW;//tempTex.SetPixel(i, j, cW);
                        }
                    }
                }
            }
            //tempTex.Apply();
            float highestAlpha = 0f;
            for (int i = 0; i < returnTex2.GetLength(0); i++)
            {
                for (int j = 0; j < returnTex2.GetLength(1); j++)
                {
                    returnTex2[i, j] = tempTex[i, j];//returnTex.SetPixel(i, j, tempTex.GetPixel(i, j));
                    if (tempTex[i, j].a > highestAlpha)
                        highestAlpha = tempTex[i, j].a;
                }
            }
            //returnTex.Apply();
        }

        for (int i = 0; i < returnTex2.GetLength(0); i++)
        {
            for (int j = 0; j < returnTex2.GetLength(1); j++)
            {
                returnTex[i, j, 0] = returnTex2[i, j].r;
                returnTex[i, j, 1] = returnTex2[i, j].g;
                returnTex[i, j, 2] = returnTex2[i, j].b;
                returnTex[i, j, 3] = returnTex2[i, j].a;
            }
        }

        return returnTex;
    }

    private int modeR = 9;
    private int modeRNormalize = 0;

    //Texture2D CalculateDistanceField(Texture2D ogTex, Texture2D bgTex, Color outlineColor, float colorTolerance)
    float[,,] CalculateDistanceField(float[,,] ogTex, float[,,] bgTex, Color outlineColor, float colorTolerance)
    {
        /*
            Distance Field calculated by nearest outline pixel to it.
            0.0 = not inside shape, 0.5 = boundary of shape, 1.0 = inside of shape
            neighborhoodRange = max allowed distance to register
         */

        //Texture2D dfTex = new Texture2D(ogTex.width, ogTex.height);
        float[,,] dfTex = new float[ogTex.GetLength(0), ogTex.GetLength(1), 4];
        float[,] dfR = new float[ogTex.GetLength(0), ogTex.GetLength(1)];
        float[,] dfG = new float[ogTex.GetLength(0), ogTex.GetLength(1)];
        float[,] dfB = new float[ogTex.GetLength(0), ogTex.GetLength(1)];
        float[,] dfA = new float[ogTex.GetLength(0), ogTex.GetLength(1)];

        for (int x = 0; x < dfTex.GetLength(0); x++)
        {
            for (int y = 0; y < dfTex.GetLength(1); y++)
            {
                dfTex[x, y, 0] = 0;//dfTex.SetPixel(x, y, Color.black);
                dfTex[x, y, 1] = 0;
                dfTex[x, y, 2] = 0;
                dfTex[x, y, 3] = 1;
                dfR[x, y] = 0f;
                dfG[x, y] = 0f;
                dfB[x, y] = 0f;
                dfA[x, y] = 0f;
            }
        }
        // only need to do this for 1 layer
        dfR = CalculateLayer(ogTex, outlineColor, colorTolerance, modeR, modeRNormalize, dfR);

        for (int x = 0; x < dfTex.GetLength(0); x++)
        {
            for (int y = 0; y < dfTex.GetLength(1); y++)
            {
                dfTex[x, y, 0] = dfR[x, y];
                dfTex[x, y, 1] = dfG[x, y];
                dfTex[x, y, 2] = dfB[x, y];
                dfTex[x, y, 3] = dfA[x, y];
                //dfTex.SetPixel(x, y, new Color(dfR[x, y], dfG[x, y], dfB[x, y], dfA[x, y]));
            }
        }
        //dfTex.Apply();

        return dfTex;
    }

    private float[,] CalculateLayer(float[,,] ogTex, Color outlineColor, float colorTolerance, int modeC, int modeCNormalize, float[,] dfR)
    {
        if (df_type == 0)
        {
            dfR = CalculateDistanceFieldLayerOptimized01(ConvertFloatToTexture(ogTex), outlineColor, colorTolerance, (int)(ogTex.GetLength(0) * 0.025f));     // * 0.1f...
        }
        else if (df_type == 1)
        {
            // standard signed Distance Field = boundary of every outline is always 0.5
            dfR = CalculateNormalSignedDistanceFieldLayer(ConvertFloatToTexture(ogTex), outlineColor, colorTolerance, (int)(ogTex.GetLength(0) * 0.25f));
        }
        else if (df_type == 2)
        {
            // processed signed Distance Field = inside center of every outline is always 1
            dfR = CalculateRelativeSignedDistanceFieldLayer(ogTex, outlineColor, colorTolerance, (int)(ogTex.GetLength(0) * 0.25f));
        }

        return dfR;
    }

    private float[,] CalculateLayer(Texture2D ogTex, Color outlineColor, float colorTolerance, int modeC, int modeCNormalize, float[,] dfR)
    {
        if (df_type == 0)
        {
            dfR = CalculateDistanceFieldLayerOptimized01(ogTex, outlineColor, colorTolerance, (int)(ogTex.width * 0.025f));     // * 0.1f...
        }
        else if (df_type == 1)
        {
            // standard signed Distance Field = boundary of every outline is always 0.5
            dfR = CalculateNormalSignedDistanceFieldLayer(ogTex, outlineColor, colorTolerance, (int)(ogTex.width * 0.25f));
        }
        else if (df_type == 2)
        {
            // processed signed Distance Field = inside center of every outline is always 1
            dfR = CalculateRelativeSignedDistanceFieldLayer(ogTex, outlineColor, colorTolerance, (int)(ogTex.width * 0.25f));
        }

        return dfR;
    }

    private float[,] CalculateDistanceFieldLayerOptimized01(Texture2D ogTex, Color outlineColor, float colorTolerance, int neighborhoodRange)
    {
        float[,] returnLayer = new float[ogTex.width, ogTex.height];
        Vector2[,] nearestOutline = new Vector2[ogTex.width, ogTex.height];

        // skeletonize texture first
        float[,] ogLayer = new float[ogTex.width, ogTex.height];
        for (int x = 0; x < ogTex.width; x++)
        {
            for (int y = 0; y < ogTex.height; y++)
            {
                Color c0 = ogTex.GetPixel(x, y);
                Color c2 = outlineColor;
                if (Vector3.Distance(
                    new Vector3(c0.r, c0.g, c0.b),
                    new Vector3(c2.r, c2.g, c2.b))
                    <= colorTolerance && c0.a > 0.1f)
                {
                    // current pixel is inside the outline color, find distance from the outline
                    ogLayer[x, y] = 1.0f;
                }
                else
                {
                    ogLayer[x, y] = 0.0f;
                }
            }
        }
        ogLayer = Skeletonize2DTexture(ogTex, ogLayer);

        List<Vector2> listOfPoints = new List<Vector2>();
        for (int i = 0; i < ogTex.width; i++)
        {
            for (int j = 0; j < ogTex.height; j++)
            {
                if (ogLayer[i, j] == 1.0f)
                {
                    listOfPoints.Add(new Vector2(i, j));
                    returnLayer[i, j] = 1.0f;
                    nearestOutline[i, j] = new Vector2(i, j);
                }
                else
                {
                    returnLayer[i, j] = -1.0f;
                    nearestOutline[i, j] = new Vector2(-1f, -1f);
                }
            }
        }
        int distance = 0;
        while (listOfPoints.Count > 0 && distance < ogTex.width)
        {
            // start from end to the front, pop off point and set it's neighbor cells to 
            distance++;
            int listIndex = listOfPoints.Count - 1;
            for (int i = listIndex; i >= 0; i--)
            {
                Vector2 point = listOfPoints[i];
                listOfPoints.RemoveAt(i);
                Vector2[] pNeigh = new Vector2[4];
                pNeigh[0] = point + new Vector2(1, 0);
                pNeigh[1] = point + new Vector2(-1, 0);
                pNeigh[2] = point + new Vector2(0, 1);
                pNeigh[3] = point + new Vector2(0, -1);
                for (int j = 0; j < pNeigh.Length; j++)
                {
                    int x1 = (int)pNeigh[j].x; int y1 = (int)pNeigh[j].y;
                    if (x1 < 0 || x1 >= ogTex.width || y1 < 0 || y1 >= ogTex.height)
                    {
                        continue;
                    }
                    if (returnLayer[x1, y1] == -1f)
                    {
                        nearestOutline[x1, y1] = nearestOutline[(int)point.x, (int)point.y];
                        float dis = Vector2.Distance(nearestOutline[(int)point.x, (int)point.y], pNeigh[j]);
                        returnLayer[x1, y1] = Mathf.Max(0f, 1.0f - (dis / neighborhoodRange));
                        listOfPoints.Add(pNeigh[j]);
                    }
                    else
                    {
                        float dis = Vector2.Distance(nearestOutline[(int)point.x, (int)point.y], pNeigh[j]);
                        float finalDis = Mathf.Max(0f, 1.0f - (dis / neighborhoodRange));
                        if (finalDis > returnLayer[x1, y1])
                        {
                            nearestOutline[x1, y1] = nearestOutline[(int)point.x, (int)point.y];
                            returnLayer[x1, y1] = finalDis;
                            listOfPoints.Add(pNeigh[j]);
                        }
                    }

                }
            }
        }

        return returnLayer;
    }

    private bool DistanceCompareLessThan(Vector3 disVec, float dis)
    {
        if (Vector3.SqrMagnitude(disVec) <= dis * dis)
        {
            return true;
        }
        return false;
    }

    private float[,] CalculateNormalSignedDistanceFieldLayer(Texture2D ogTex, Color outlineColor, float colorTolerance, int neighborhoodRange)
    {
        return CalculateNormalSignedDistanceFieldLayer(ConvertTextureToFloat(ogTex), outlineColor, colorTolerance, neighborhoodRange);
    }

    private float[,] CalculateNormalSignedDistanceFieldLayer(float[,,] ogTex, Color outlineColor, float colorTolerance, int neighborhoodRange)
    {
        /*
            Calculate a normal Signed Distance Field, not enforcing 1.0 at center of each curve.
         */

        float[,] dfLayer = new float[ogTex.GetLength(0), ogTex.GetLength(1)];

        float[,] ogLayer = new float[ogTex.GetLength(0), ogTex.GetLength(1)];
        float[,] returnLayer = new float[ogTex.GetLength(0), ogTex.GetLength(1)];
        Vector2[,] nearestOutline = new Vector2[ogTex.GetLength(0), ogTex.GetLength(1)];


        // find boundaries
        for (int x = 0; x < ogTex.GetLength(0); x++)
        {
            Color c2 = outlineColor;
            for (int y = 0; y < ogTex.GetLength(1); y++)
            {
                Color c0 = new Color(ogTex[x, y, 0], ogTex[x, y, 1], ogTex[x, y, 2], ogTex[x, y, 3]);// ogTex.GetPixel(x, y);

                /*if (Vector3.Distance(
                    new Vector3(c0.r, c0.g, c0.b),
                    new Vector3(c2.r, c2.g, c2.b))
                    <= colorTolerance && c0.a > 0.1f)*/
                if (DistanceCompareLessThan(new Vector3(c0.r, c0.g, c0.b) - new Vector3(c2.r, c2.g, c2.b), colorTolerance) == true && c0.a > 0.1f)
                {
                    // current pixel is inside the outline color, find whether this is the boundary
                    float[] cn_0 = new float[] { ogTex[Mathf.Max(0, x - 1), y, 0], ogTex[Mathf.Max(0, x - 1), y, 1], ogTex[Mathf.Max(0, x - 1), y, 2], ogTex[Mathf.Max(0, x - 1), y, 3] };
                    float[] cn_1 = new float[] { ogTex[Mathf.Min(ogTex.GetLength(0) - 1, x + 1), y, 0], ogTex[Mathf.Min(ogTex.GetLength(0) - 1, x + 1), y, 1], ogTex[Mathf.Min(ogTex.GetLength(0) - 1, x + 1), y, 2], ogTex[Mathf.Min(ogTex.GetLength(0) - 1, x + 1), y, 3] };
                    float[] cn_2 = new float[] { ogTex[x, Mathf.Max(0, y - 1), 0], ogTex[x, Mathf.Max(0, y - 1), 1], ogTex[x, Mathf.Max(0, y - 1), 2], ogTex[x, Mathf.Max(0, y - 1), 3] };
                    float[] cn_3 = new float[] { ogTex[x, Mathf.Min(ogTex.GetLength(1) - 1, y + 1), 0], ogTex[x, Mathf.Min(ogTex.GetLength(1) - 1, y + 1), 1], ogTex[x, Mathf.Min(ogTex.GetLength(1) - 1, y + 1), 2], ogTex[x, Mathf.Min(ogTex.GetLength(1) - 1, y + 1), 3] };
                    if (
                            (DistanceCompareLessThan(new Vector3(cn_0[0], cn_0[1], cn_0[2]) - new Vector3(c2[0], c2[1], c2[2]), colorTolerance) == true && cn_0[3] > 0.1f) == false
                            || (DistanceCompareLessThan(new Vector3(cn_1[0], cn_1[1], cn_1[2]) - new Vector3(c2[0], c2[1], c2[2]), colorTolerance) == true && cn_1[3] > 0.1f) == false
                            || (DistanceCompareLessThan(new Vector3(cn_2[0], cn_2[1], cn_2[2]) - new Vector3(c2[0], c2[1], c2[2]), colorTolerance) == true && cn_2[3] > 0.1f) == false
                            || (DistanceCompareLessThan(new Vector3(cn_3[0], cn_3[1], cn_3[2]) - new Vector3(c2[0], c2[1], c2[2]), colorTolerance) == true && cn_3[3] > 0.1f) == false)
                    {
                        ogLayer[x, y] = 0.5f;
                    }
                    else
                    {
                        ogLayer[x, y] = 0.0f;
                    }
                }
                else
                {
                    ogLayer[x, y] = 0.0f;
                }
            }
        }

        List<Vector2> listOfPoints = new List<Vector2>();
        for (int i = 0; i < ogTex.GetLength(0); i++)
        {
            for (int j = 0; j < ogTex.GetLength(1); j++)
            {
                if (ogLayer[i, j] == 0.5f)
                {
                    listOfPoints.Add(new Vector2(i, j));
                    returnLayer[i, j] = 0.0f;
                    nearestOutline[i, j] = new Vector2(i, j);
                }
                else
                {
                    returnLayer[i, j] = -1.0f;
                    nearestOutline[i, j] = new Vector2(-1f, -1f);
                }
            }
        }
        int distance = 0;
        // Calculate which "line" pixel is closest to every other pixel in the sprite.
        while (listOfPoints.Count > 0 && distance < ogTex.GetLength(0))
        {
            // start from end to the front, pop off point and set it's neighbor cells to 
            distance++;
            int listIndex = listOfPoints.Count - 1;
            for (int i = listIndex; i >= 0; i--)
            {
                Vector2 point = listOfPoints[i];
                listOfPoints.RemoveAt(i);
                Vector2[] pNeigh = new Vector2[4];
                pNeigh[0] = point + new Vector2(1, 0);
                pNeigh[1] = point + new Vector2(-1, 0);
                pNeigh[2] = point + new Vector2(0, 1);
                pNeigh[3] = point + new Vector2(0, -1);
                for (int j = 0; j < pNeigh.Length; j++)
                {
                    int x1 = (int)pNeigh[j].x; int y1 = (int)pNeigh[j].y;
                    if (x1 < 0 || x1 >= ogTex.GetLength(0) || y1 < 0 || y1 >= ogTex.GetLength(1))
                    {
                        continue;
                    }
                    if (returnLayer[x1, y1] == -1f)
                    {
                        nearestOutline[x1, y1] = nearestOutline[(int)point.x, (int)point.y];
                        float dis = Vector2.Distance(nearestOutline[(int)point.x, (int)point.y], pNeigh[j]);
                        returnLayer[x1, y1] = Mathf.Max(0f, dis);
                        listOfPoints.Add(pNeigh[j]);
                    }
                    else
                    {
                        float dis = Vector2.Distance(nearestOutline[(int)point.x, (int)point.y], pNeigh[j]);
                        float finalDis = Mathf.Max(0f, dis);
                        if (finalDis < returnLayer[x1, y1])
                        {
                            nearestOutline[x1, y1] = nearestOutline[(int)point.x, (int)point.y];
                            returnLayer[x1, y1] = finalDis;
                            listOfPoints.Add(pNeigh[j]);
                        }
                    }

                }
            }
        }

        // OK... returnLayer[x,y] has distance from a boundary point. Calculate new Distance Field now.
        for (int i = 0; i < ogTex.GetLength(0); i++)
        {
            for (int j = 0; j < ogTex.GetLength(1); j++)
            {
                bool isInside = false;
                Color c0 = new Color(ogTex[i, j, 0], ogTex[i, j, 1], ogTex[i, j, 2], ogTex[i, j, 3]);//ogTex.GetPixel(i, j);
                Color c2 = outlineColor;
                if (Vector3.Distance(
                    new Vector3(c0.r, c0.g, c0.b),
                    new Vector3(c2.r, c2.g, c2.b))
                    <= colorTolerance && c0.a > 0.1f)
                {
                    isInside = true;
                }
                if (returnLayer[i, j] == 0.0f)
                {
                    // is the boundary
                    dfLayer[i, j] = 0.5f;
                }
                else if (returnLayer[i, j] > 0.0f && isInside == true)
                {
                    // is inside the outline
                    dfLayer[i, j] = 0.5f + (returnLayer[i, j] / (float)neighborhoodRange);
                    dfLayer[i, j] = Mathf.Max(0.5f, dfLayer[i, j]);
                    dfLayer[i, j] = Mathf.Min(1.0f, dfLayer[i, j]);
                }
                else if (returnLayer[i, j] > 0.0f && isInside == false)
                {
                    // is outside the outline
                    dfLayer[i, j] = 0.5f - (returnLayer[i, j] / (float)neighborhoodRange);
                    dfLayer[i, j] = Mathf.Max(0.0f, dfLayer[i, j]);
                    dfLayer[i, j] = Mathf.Min(0.5f, dfLayer[i, j]);
                }
                else
                {
                    dfLayer[i, j] = 0.0f;
                }
            }
        }

        return dfLayer;
    }

    private float[,] CalculateRelativeSignedDistanceFieldLayer(Texture2D ogTex, Color outlineColor, float colorTolerance, int neighborhoodRange)
    {
        float[,,] ogTexArray = ConvertTextureToFloat(ogTex);
        return CalculateRelativeSignedDistanceFieldLayer(ogTexArray, outlineColor, colorTolerance, neighborhoodRange);
    }

    private float[,] CalculateRelativeSignedDistanceFieldLayer(float[,,] ogTex, Color outlineColor, float colorTolerance, int neighborhoodRange)
    {
        /* Same as CalculateDistanceFieldLayerOptimized01, but with 0.5 indicating the Discontinuity Edge of an outline.
            Value at each DF cell = n, 
                where f = width * pow(2, 10 * (0.5 - n)), 
                where f = distance of cell from center of an outline, 
                where width = width of outline between cell and nearest center of outline. */

        float[,] dfLayer = new float[ogTex.GetLength(0), ogTex.GetLength(1)];

        float time1 = Time.realtimeSinceStartup;

        float[,] returnLayer = new float[ogTex.GetLength(0), ogTex.GetLength(1)];
        Vector2[,] nearestOutline = new Vector2[ogTex.GetLength(0), ogTex.GetLength(1)];

        // skeletonize texture first
        float[,] ogLayer = new float[ogTex.GetLength(0), ogTex.GetLength(1)];
        float[,] ogLayer2 = new float[ogTex.GetLength(0), ogTex.GetLength(1)];
        for (int x = 0; x < ogTex.GetLength(0); x++)
        {
            for (int y = 0; y < ogTex.GetLength(1); y++)
            {
                Color c0 = new Color(ogTex[x, y, 0], ogTex[x, y, 1], ogTex[x, y, 2], ogTex[x, y, 3]);//ogTex.GetPixel(x, y);
                Color c2 = outlineColor;
                if (Vector3.Distance(
                    new Vector3(c0.r, c0.g, c0.b),
                    new Vector3(c2.r, c2.g, c2.b))
                    <= colorTolerance && c0.a > 0.1f)
                {
                    // current pixel is inside the outline color, find distance from the outline
                    ogLayer[x, y] = 1.0f;
                    ogLayer2[x, y] = 1.0f;
                }
                else
                {
                    ogLayer[x, y] = 0.0f;
                    ogLayer2[x, y] = 0.0f;
                }
            }
        }
        ogLayer = Skeletonize2DTexture(ogTex, ogLayer);

        float[,] sdfLayer = CalculateNormalSignedDistanceFieldLayer(ogTex, outlineColor, colorTolerance, neighborhoodRange);
        ogLayer = PruneSkeleton(ogLayer, sdfLayer);

        List<Vector2> listOfPoints = new List<Vector2>();
        for (int i = 0; i < ogTex.GetLength(0); i++)
        {
            for (int j = 0; j < ogTex.GetLength(1); j++)
            {
                if (ogLayer[i, j] == 1.0f)
                {
                    listOfPoints.Add(new Vector2(i, j));
                    returnLayer[i, j] = 0.0f;
                    nearestOutline[i, j] = new Vector2(i, j);
                }
                else
                {
                    returnLayer[i, j] = -1.0f;
                    nearestOutline[i, j] = new Vector2(-1f, -1f);
                }
            }
        }
        int distance = 0;
        // Calculate which "line" pixel is closest to every other pixel in the sprite.
        while (listOfPoints.Count > 0 && distance < ogTex.GetLength(0))
        {
            // start from end to the front, pop off point and set it's neighbor cells to 
            distance++;
            int listIndex = listOfPoints.Count - 1;
            for (int i = listIndex; i >= 0; i--)
            {
                Vector2 point = listOfPoints[i];
                listOfPoints.RemoveAt(i);
                Vector2[] pNeigh = new Vector2[4];
                pNeigh[0] = point + new Vector2(1, 0);
                pNeigh[1] = point + new Vector2(-1, 0);
                pNeigh[2] = point + new Vector2(0, 1);
                pNeigh[3] = point + new Vector2(0, -1);
                for (int j = 0; j < pNeigh.Length; j++)
                {
                    int x1 = (int)pNeigh[j].x; int y1 = (int)pNeigh[j].y;
                    if (x1 < 0 || x1 >= ogTex.GetLength(0) || y1 < 0 || y1 >= ogTex.GetLength(1))
                    {
                        continue;
                    }
                    if (returnLayer[x1, y1] == -1f)
                    {
                        nearestOutline[x1, y1] = nearestOutline[(int)point.x, (int)point.y];
                        float dis = Vector2.Distance(nearestOutline[(int)point.x, (int)point.y], pNeigh[j]);
                        // nearest outline might be a non-discrete value
                        if (rasterInterpMode == 1
                            && nearestOutline[(int)point.x, (int)point.y].x > 0 && nearestOutline[(int)point.x, (int)point.y].x < ogTex.GetLength(0) - 1
                            && nearestOutline[(int)point.x, (int)point.y].y > 0 && nearestOutline[(int)point.x, (int)point.y].y < ogTex.GetLength(1) - 1)
                        {
                            Vector2 ogNearestOutline = nearestOutline[(int)point.x, (int)point.y];
                            Vector2 currentPoint = pNeigh[j];

                            Vector2 ogNearestOutline2 = ogNearestOutline + new Vector2(0, 0);
                            for (int x = (int)ogNearestOutline.x - 1; x <= ogNearestOutline.x + 1; x++)
                            {
                                for (int y = (int)ogNearestOutline.y - 1; y <= ogNearestOutline.y + 1; y++)
                                {
                                    if (x == ogNearestOutline.x && y == ogNearestOutline.y)
                                        continue;
                                    if ((Vector2.Distance(currentPoint, ogNearestOutline2) > Vector2.Distance(currentPoint, new Vector2(x, y)) && ogLayer[x, y] == 1.0f)
                                        || (ogNearestOutline2.x == ogNearestOutline.x && ogNearestOutline2.y == ogNearestOutline.y && ogLayer[x, y] == 1.0f))
                                        ogNearestOutline2 = new Vector2(x, y);
                                }
                            }
                            // simple cheat - check a limited number of points between these two points, if a high-enough resolution, should be OK
                            Vector2 nearestNewPoint = ogNearestOutline;
                            for (int x = 0; x <= 10; x++)
                            {
                                Vector2 newPoint = ogNearestOutline - (ogNearestOutline - ogNearestOutline2) * 0.10f * (float)(x);
                                if (Vector2.Distance(currentPoint, nearestNewPoint) > Vector2.Distance(currentPoint, newPoint))
                                    nearestNewPoint = newPoint;
                            }
                            dis = Vector2.Distance(currentPoint, nearestNewPoint);
                        }

                        returnLayer[x1, y1] = Mathf.Max(0f, dis);
                        listOfPoints.Add(pNeigh[j]);
                    }
                    else
                    {
                        float dis = Vector2.Distance(nearestOutline[(int)point.x, (int)point.y], pNeigh[j]);
                        // nearest outline might be a non-discrete value
                        if (rasterInterpMode == 1
                            && nearestOutline[(int)point.x, (int)point.y].x > 0 && nearestOutline[(int)point.x, (int)point.y].x < ogTex.GetLength(0) - 1
                            && nearestOutline[(int)point.x, (int)point.y].y > 0 && nearestOutline[(int)point.x, (int)point.y].y < ogTex.GetLength(1) - 1)
                        {
                            Vector2 ogNearestOutline = nearestOutline[(int)point.x, (int)point.y];
                            Vector2 currentPoint = pNeigh[j];

                            Vector2 ogNearestOutline2 = ogNearestOutline + new Vector2(0, 0);
                            for (int x = (int)ogNearestOutline.x - 1; x <= ogNearestOutline.x + 1; x++)
                            {
                                for (int y = (int)ogNearestOutline.y - 1; y <= ogNearestOutline.y + 1; y++)
                                {
                                    if (x == ogNearestOutline.x && y == ogNearestOutline.y)
                                        continue;
                                    if ((Vector2.Distance(currentPoint, ogNearestOutline2) > Vector2.Distance(currentPoint, new Vector2(x, y)) && ogLayer[x, y] == 1.0f)
                                        || (ogNearestOutline2.x == ogNearestOutline.x && ogNearestOutline2.y == ogNearestOutline.y && ogLayer[x, y] == 1.0f))
                                        ogNearestOutline2 = new Vector2(x, y);
                                }
                            }
                            // simple cheat - check a limited number of points between these two points, if a high-enough resolution, should be OK
                            Vector2 nearestNewPoint = ogNearestOutline;
                            for (int x = 0; x <= 10; x++)
                            {
                                Vector2 newPoint = ogNearestOutline - (ogNearestOutline - ogNearestOutline2) * 0.10f * (float)(x);
                                if (Vector2.Distance(currentPoint, nearestNewPoint) > Vector2.Distance(currentPoint, newPoint))
                                    nearestNewPoint = newPoint;
                            }
                            dis = Vector2.Distance(currentPoint, nearestNewPoint);
                        }
                        float finalDis = Mathf.Max(0f, dis);
                        if (finalDis < returnLayer[x1, y1])
                        {
                            nearestOutline[x1, y1] = nearestOutline[(int)point.x, (int)point.y];
                            returnLayer[x1, y1] = finalDis;
                            listOfPoints.Add(pNeigh[j]);
                        }
                    }

                }
            }
        }

        // calculate "signed" value, where 0.5 = Discontinuity Edge
        // (ie. "line thickness" is calculated here:
        //      * for pixel (x,y), distance to nearest line point is stored in returnLayer[x,y]
        //      * for pixel (x,y), it is in the original outline (including thickness) if ogLayer2[x,y] == 1.0
        //      * for pixel (x,y), the nearest line point coordinates is stored in nearestOutline[x,y]
        // advice: "... better solution to calculate line thickness is... for each point on line, increase radial range until boundary is found, returns smallest 'thickness' at that point..."
        float[,] ogLayer3 = new float[ogTex.GetLength(0), ogTex.GetLength(1)];
        for (int i = 0; i < ogTex.GetLength(0); i++)
        {
            for (int j = 0; j < ogTex.GetLength(1); j++)
            {
                Vector2 pix = new Vector2((int)nearestOutline[i, j].x, (int)nearestOutline[i, j].y);
                if (ogLayer3[(int)pix.x, (int)pix.y] != 0)
                {
                    dfLayer[i, j] = ogLayer3[(int)pix.x, (int)pix.y];
                    continue;
                }
                float lineWidth = ogTex.GetLength(0);
                float radius = ogTex.GetLength(0) * 0.1f;

                for (int x = (int)Mathf.Max(0, pix.x - radius); x < Mathf.Min(ogTex.GetLength(0), pix.x + radius); x++)
                {
                    for (int y = (int)Mathf.Max(0, pix.y - radius); y < Mathf.Min(ogTex.GetLength(0), pix.y + radius); y++)
                    {
                        if (ogLayer2[x, y] == 0.0f)  // is not in outline anymore
                        {
                            // issue: if skeletonization is not DIRECTLY in the middle, some thicknesses will be smaller than they should be...
                            // alternative: need to find 2 non-concurrent gaps, then average the 2. 
                            if (lineWidth != 0f && Vector2.Distance(pix, new Vector2(x, y)) < lineWidth)
                            {
                                lineWidth = Vector2.Distance(pix, new Vector2(x, y));
                            }
                        }
                    }
                }


                if (lineWidth == 0f)
                {
                    Debug.Log("width found to be 0f... pixel = " + pix.x + ", " + pix.y);
                }
                ogLayer3[(int)pix.x, (int)pix.y] = lineWidth;
                dfLayer[i, j] = lineWidth;
            }
        }

        dfLayer = CalculateBoxBlurWideLayerNon0(dfLayer, 5);

        for (int i = 0; i < ogTex.GetLength(0); i++)
        {
            for (int j = 0; j < ogTex.GetLength(1); j++)
            {
                float dfV = -1f;

                float lineWidth = dfLayer[i, j];
                float lineDis = returnLayer[i, j];

                dfV = Mathf.Log(lineDis / lineWidth) / Mathf.Log(2);
                dfV = (dfV - 5) / (-10);

                dfV = Mathf.Max(0, dfV);
                dfV = Mathf.Min(1, dfV);

                dfLayer[i, j] = dfV;

                // for non-radial parts of an outline, SDF retains original shape better than RSDF. Use below to fallback to SDF.
                if (dfLayer[i, j] < 0.5f && sdfLayer[i, j] >= 0.5f)
                {
                    dfLayer[i, j] = (dfLayer[i, j] + sdfLayer[i, j]) * 0.5f;
                    if (dfLayer[i, j] < 0.5f)
                    {
                        dfLayer[i, j] = 0.50f + (sdfLayer[i, j] - 0.5f) * 0.1f;
                    }

                }
                else if (dfLayer[i, j] >= 0.5f && sdfLayer[i, j] < 0.5f)
                {
                    dfLayer[i, j] = (dfLayer[i, j] + sdfLayer[i, j]) * 0.5f;
                    if (dfLayer[i, j] >= 0.5f)
                    {
                        dfLayer[i, j] = 0.50f - (sdfLayer[i, j] - 0.5f) * 0.1f;
                    }
                }

            }
        }

        return dfLayer;
    }

    private float[,] CalculateBoxBlurWideLayerNon0(float[,] ogTex, int range)
    {
        float[,] dfLayer = new float[ogTex.GetLength(0), ogTex.GetLength(1)];
        for (int x = 0; x < dfLayer.GetLength(0); x++)
        {
            for (int y = 0; y < dfLayer.GetLength(1); y++)
            {
                float total = 0f;
                float count = 0f;
                for (int i = Mathf.Max(0, x - range); i < Mathf.Min(dfLayer.GetLength(0) - 1, x + range); i++)
                {
                    for (int j = Mathf.Max(0, y - range); j < Mathf.Min(dfLayer.GetLength(1) - 1, y + range); j++)
                    {
                        if (ogTex[i, j] > 0)
                        {
                            count++;
                            total += ogTex[i, j];
                        }
                    }
                }
                if (count > 0)
                {
                    dfLayer[x, y] = total / count;
                }
                else
                {
                    dfLayer[x, y] = ogTex[x, y];
                }
            }
        }
        return dfLayer;
    }

    private float[,] PruneSkeleton(float[,] ogLayer, float[,] sdfLayer)
    {
        // https://en.wikipedia.org/wiki/Pruning_(morphology)
        // ... matlab proprietary code, can't find any good explainations written in plain english...
        // alt: use SDF: increase cutoff slowly to prune out outstanding branches. Compare to prior step every time. When difference is too great, stop.

        int width = ogLayer.GetLength(0);
        int height = ogLayer.GetLength(1);
        float[,] returnLayer = new float[width, height];
        float[,] nextLayer = new float[width, height];
        float[,] tempsdfLayer = new float[width, height];
        float diff = 0;
        float cutoff = 0.5f;
        float totalLines = 0;
        float n = 0;

        for (int i = 0; i < width; i++)
        {
            for (int j = 0; j < height; j++)
            {
                tempsdfLayer[i, j] = Mathf.Sqrt(sdfLayer[i, j]);        // to not lose skinny lines too quickly
                returnLayer[i, j] = ogLayer[i, j];
                nextLayer[i, j] = ogLayer[i, j];
                if (nextLayer[i, j] == 1.0f)
                {
                    totalLines++;
                }
            }
        }

        for (n = 0.7f; n < 1f; n = n + 0.02f)
        {
            diff = 0;
            cutoff = n;
            for (int i = 0; i < width; i++)
            {
                for (int j = 0; j < height; j++)
                {
                    if (nextLayer[i, j] == 1.0f && tempsdfLayer[i, j] < cutoff)
                    {
                        nextLayer[i, j] = 0.0f;
                        diff++;
                    }
                }
            }
            if ((diff / totalLines) > 0.1f)
            {
                // we pruned too much, break
                break;
            }
            totalLines = 0;
            for (int i = 0; i < width; i++)
            {
                for (int j = 0; j < height; j++)
                {
                    returnLayer[i, j] = nextLayer[i, j];
                    if (nextLayer[i, j] == 1.0f)
                    {
                        totalLines++;
                    }
                }
            }
        }

        return returnLayer;
    }

    private float[,] Skeletonize2DTexture(Texture2D ogTex, float[,] ogLayer)
    {
        return Skeletonize2DTexture(ConvertTextureToFloat(ogTex), ogLayer);
    }

    private float[,] Skeletonize2DTexture(float[,,] ogTex, float[,] ogLayer)
    {
        // process: "skeletonize" or "thin" algorithms (matlab, python) already accomplish this...
        // see https://github.com/scikit-image/scikit-image/blob/main/skimage/morphology/_skeletonize.py
        // https://gist.github.com/PavelTorgashov/6a26cd287c9447aefd1c9397c55580f9
        // slightly more accurate "alternating" code described here (better matches the original paper description): https://rosettacode.org/wiki/Zhang-Suen_thinning_algorithm
        /* Notes:
            * Tried both the alternating code from Github (PavelTorgashov) and from original paper (rosettacode). Both were better in different aspects, but neither were perfect.
            * Tried using both AT THE SAME TIME, almost good results, but conflicts with each other and sometimes causes lines that are thick.
            * Tried using both SEPARATELY, then combine (A || B == Out), this works the best.
            * Also tried some recent "improved" updates of Zhang-Suen Thinning Algorithm (1984), these are easy to read if they follow the structure of the 1984 method, but also show not to work well in both HD and SD input.
            * (Later) NO NO NO, I misread the original paper's alternating parameters: if I use them correctly, it works great all by itself.
         */

        float[,] ogLayer2a = new float[ogTex.GetLength(0), ogTex.GetLength(1)];
        float[,] ogLayer3a = new float[ogTex.GetLength(0), ogTex.GetLength(1)];
        for (int x = 1; x < ogTex.GetLength(0) - 1; x++)
        {
            for (int y = 1; y < ogTex.GetLength(1) - 1; y++)
            {
                ogLayer2a[x, y] = ogLayer[x, y];
                ogLayer3a[x, y] = ogLayer[x, y];
            }
        }
        for (int i = 0; i < 40; i++)
        {
            float[,] ogLayer2 = new float[ogTex.GetLength(0), ogTex.GetLength(1)];
            float[,] ogLayer3 = new float[ogTex.GetLength(0), ogTex.GetLength(1)];
            bool stopIter = true;

            for (int x = 1; x < ogTex.GetLength(0) - 1; x++)
            {
                for (int y = 1; y < ogTex.GetLength(1) - 1; y++)
                {
                    ogLayer2[x, y] = ogLayer2a[x, y];
                    ogLayer3[x, y] = ogLayer3a[x, y];
                    if (ogLayer2a[x, y] == 1.0f)
                    {
                        float sum = 0;
                        float p2 = ogLayer2a[x, y - 1], p3 = ogLayer2a[x + 1, y - 1], p4 = ogLayer2a[x + 1, y],
                              p5 = ogLayer2a[x + 1, y + 1], p6 = ogLayer2a[x, y + 1], p7 = ogLayer2a[x - 1, y + 1],
                              p8 = ogLayer2a[x - 1, y], p9 = ogLayer2a[x - 1, y - 1];
                        sum = p2 + p3 + p4 + p5 + p6 + p7 + p8 + p9;
                        if (sum >= 2 && sum <= 6)
                        {
                            sum = 0;
                            if (p2 == 0 && p3 == 1)
                                sum++;
                            if (p3 == 0 && p4 == 1)
                                sum++;
                            if (p4 == 0 && p5 == 1)
                                sum++;
                            if (p5 == 0 && p6 == 1)
                                sum++;
                            if (p6 == 0 && p7 == 1)
                                sum++;
                            if (p7 == 0 && p8 == 1)
                                sum++;
                            if (p8 == 0 && p9 == 1)
                                sum++;
                            if (p9 == 0 && p2 == 1)
                                sum++;
                            if (sum == 1)
                            {
                                // to prevent both left and right of a line from getting cut at the same iteration.
                                // bug in original source above caused large gaps to sometimes appear. Mostly fixed with explaination at: https://rosettacode.org/wiki/Zhang-Suen_thinning_algorithm
                                if (i % 2 == 0)
                                {
                                    if ((p2 == 1 && p4 == 1 && p6 == 1) == false && (p4 == 1 && p6 == 1 && p8 == 1) == false)
                                    {
                                        ogLayer2[x, y] = 0.0f;
                                        stopIter = false;
                                    }
                                }
                                else
                                {
                                    if ((p2 == 1 && p4 == 1 && p8 == 1) == false && (p2 == 1 && p6 == 1 && p8 == 1) == false)
                                    {
                                        ogLayer2[x, y] = 0.0f;
                                        stopIter = false;
                                    }
                                }
                            }
                        }
                    }
                    if (ogLayer3a[x, y] == 1.0f)
                    {
                        float sum = 0;
                        float p2 = ogLayer3a[x, y - 1], p3 = ogLayer3a[x + 1, y - 1], p4 = ogLayer3a[x + 1, y],
                              p5 = ogLayer3a[x + 1, y + 1], p6 = ogLayer3a[x, y + 1], p7 = ogLayer3a[x - 1, y + 1],
                              p8 = ogLayer3a[x - 1, y], p9 = ogLayer3a[x - 1, y - 1];
                        sum = p2 + p3 + p4 + p5 + p6 + p7 + p8 + p9;
                        if (sum >= 2 && sum <= 6)
                        {
                            sum = 0;
                            if (p2 == 0 && p3 == 1)
                                sum++;
                            if (p3 == 0 && p4 == 1)
                                sum++;
                            if (p4 == 0 && p5 == 1)
                                sum++;
                            if (p5 == 0 && p6 == 1)
                                sum++;
                            if (p6 == 0 && p7 == 1)
                                sum++;
                            if (p7 == 0 && p8 == 1)
                                sum++;
                            if (p8 == 0 && p9 == 1)
                                sum++;
                            if (p9 == 0 && p2 == 1)
                                sum++;
                            if (sum == 1)
                            {
                                // to prevent both left and right of a line from getting cut at the same iteration.
                                // bug in original source above caused large gaps to sometimes appear. Mostly fixed with explaination at: https://rosettacode.org/wiki/Zhang-Suen_thinning_algorithm
                                if (i % 2 == 0)
                                {
                                    if (((p2 == 1 && p4 == 1 && p8 == 1) == false && (p4 == 1 && p6 == 1 && p8 == 1) == false))
                                    {
                                        ogLayer3[x, y] = 0.0f;
                                        stopIter = false;
                                    }
                                }
                                else
                                {
                                    if (((p2 == 1 && p4 == 1 && p8 == 1) == false && (p2 == 1 && p6 == 1 && p8 == 1) == false))
                                    {
                                        ogLayer3[x, y] = 0.0f;
                                        stopIter = false;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            for (int x = 1; x < ogTex.GetLength(0) - 1; x++)
            {
                for (int y = 1; y < ogTex.GetLength(1) - 1; y++)
                {
                    ogLayer2a[x, y] = ogLayer2[x, y];
                    ogLayer3a[x, y] = ogLayer3[x, y];
                }
            }
            if (stopIter == true)
            {
                break;
            }
        }

        for (int x = 1; x < ogTex.GetLength(0) - 1; x++)
        {
            for (int y = 1; y < ogTex.GetLength(1) - 1; y++)
            {
                if (ogLayer2a[x, y] == 1.0f)
                    ogLayer[x, y] = 1.0f;
                else
                    ogLayer[x, y] = 0.0f;
            }
        }

        return ogLayer;
    }

    Texture2D CalculateDistanceFieldInteriorColor(Texture2D ogTex, Texture2D bgTex, Color outlineColor, float colorTolerance)
    {
        Texture2D dfTex = new Texture2D(ogTex.width, ogTex.height, TextureFormat.RGBA32, false, true);

        float[,] dfCR = new float[ogTex.width, ogTex.height];
        float[,] dfCG = new float[ogTex.width, ogTex.height];
        float[,] dfCB = new float[ogTex.width, ogTex.height];
        float[,] dfCA = new float[ogTex.width, ogTex.height];
        float[,] dfB = new float[ogTex.width, ogTex.height];

        dfB = Calculate4ColorMapping(bgTex);
        dfCR = CalculateRegionDistanceField(dfB, 1.0f);
        dfCG = CalculateRegionDistanceField(dfB, 2.0f);
        dfCB = CalculateRegionDistanceField(dfB, 3.0f);
        dfCA = CalculateRegionDistanceField(dfB, 4.0f);

        for (int x = 0; x < dfTex.width; x++)
        {
            for (int y = 0; y < dfTex.height; y++)
            {
                dfTex.SetPixel(x, y, new Color(dfCR[x, y], dfCG[x, y], dfCB[x, y], dfCA[x, y]));
            }
        }
        dfTex.Apply();

        return dfTex;
    }

    private float[,] Calculate4ColorMapping(Texture2D ogTex)
    {
        /* Take input RGB texture, and return mapping for each pixel assigned to 4 different "colors". Based on "4-color mapping theorem."
         *      Example algorithm listed here: https://www.tutorialspoint.com/data_structures_algorithms/map_colouring_algorithm.htm
         *      
                ... first, have to translate input texture to a connected graph of nodes... 
                        * Set each pixel (x,y) to be assigned to a node.
                        * For each pixel,
                        *       if not already visited, and not alpha > 0.1, 
                        *           Set to new node. 
                        *           Visit all neighbouring non-visited pixels and also assign to node, set as visited. 
                        *               Stop when boundary is reached - add boundary to a new node that's connected to this node, and also set to list of nodes to visit.
                        *       repeat until all pixels have been visited.
        */

        float[,] returnLayer = new float[ogTex.width, ogTex.height];

        int[,] nodeIndexLayer = new int[ogTex.width, ogTex.height];
        bool[,] layerVisited = new bool[ogTex.width, ogTex.height];
        List<ConnectedGraph> graph = new List<ConnectedGraph>();
        int graphIndex = 0;
        float colorTolerance = 0.08f;   // value 0.08f found after testing with clown and rosy cheeks
        for (int i = 0; i < ogTex.width; i++)
        {
            for (int j = 0; j < ogTex.height; j++)
            {
                layerVisited[i, j] = false;
                nodeIndexLayer[i, j] = -1;
            }
        }
        for (int i = 0; i < ogTex.width; i++)
        {
            for (int j = 0; j < ogTex.height; j++)
            {
                if (layerVisited[i, j] == true)
                {
                    continue;
                }
                layerVisited[i, j] = true;
                List<Vector2> listOfPoints = new List<Vector2>();
                listOfPoints.Add(new Vector2(i, j));
                graphIndex++;
                ConnectedGraph newNode = new ConnectedGraph();
                newNode.nodeIndex = graphIndex;
                newNode.connections = new HashSet<int>();
                graph.Add(newNode);
                nodeIndexLayer[i, j] = graphIndex;
                while (listOfPoints.Count > 0)
                {
                    Vector2 point = listOfPoints[0];
                    listOfPoints.RemoveAt(0);

                    // when we find a boundary (not the same color), set with new node that's bi-directional connected to this node. Add to list to search. 
                    // if 2 adjacent pixels with same color are found to have different nodes, reassign to lower of the 2 nodes, remove false node from graph
                    Vector2[] pNeigh = new Vector2[4];
                    int x0 = (int)point.x; int y0 = (int)point.y;
                    pNeigh[0] = point + new Vector2(1, 0);
                    pNeigh[1] = point + new Vector2(-1, 0);
                    pNeigh[2] = point + new Vector2(0, 1);
                    pNeigh[3] = point + new Vector2(0, -1);
                    Color c1 = ogTex.GetPixel((int)point.x, (int)point.y);
                    for (int k = 0; k < pNeigh.Length; k++)
                    {
                        int x1 = (int)pNeigh[k].x; int y1 = (int)pNeigh[k].y;
                        if (x1 < 0 || x1 >= ogTex.width || y1 < 0 || y1 >= ogTex.height)
                        {
                            continue;
                        }
                        Color c2 = ogTex.GetPixel(x1, y1);
                        if (layerVisited[x1, y1] == true)
                        {
                            // check that they already have the same region index
                            if (Vector3.Distance(
                                new Vector3(c1.r, c1.g, c1.b),
                                new Vector3(c2.r, c2.g, c2.b))
                                <= colorTolerance && c1.a > 0.1f && c2.a > 0.1f
                                && nodeIndexLayer[x1, y1] == nodeIndexLayer[x0, y0])
                            {
                                continue;
                            }
                            else if (Vector3.Distance(
                                new Vector3(c1.r, c1.g, c1.b),
                                new Vector3(c2.r, c2.g, c2.b))
                                <= colorTolerance && c1.a > 0.1f && c2.a > 0.1f
                                && nodeIndexLayer[x1, y1] != nodeIndexLayer[x0, y0])
                            {
                                int nodeIndex0 = nodeIndexLayer[x0, y0];
                                int nodeIndex1 = nodeIndexLayer[x1, y1];
                                for (int m = 0; m < graph.Count; m++)
                                {
                                    if (graph[m].nodeIndex == nodeIndex0)
                                    {
                                        for (int n = 0; n < graph.Count; n++)
                                        {
                                            if (graph[n].nodeIndex == nodeIndex1)
                                            {
                                                foreach (int tempIndex in graph[n].connections)
                                                {
                                                    graph[m].connections.Add(tempIndex);
                                                }
                                                graph.RemoveAt(n);
                                                break;
                                            }
                                        }
                                        break;
                                    }
                                }
                                for (int m = 0; m < graph.Count; m++)
                                {
                                    if (graph[m].connections.Contains(nodeIndex1) == true)
                                    {
                                        graph[m].connections.Remove(nodeIndex1);
                                        graph[m].connections.Add(nodeIndex0);
                                    }
                                }
                                for (int x = 0; x < ogTex.width; x++)
                                {
                                    for (int y = 0; y < ogTex.height; y++)
                                    {
                                        if (nodeIndexLayer[x, y] == nodeIndex1)
                                        {
                                            nodeIndexLayer[x, y] = nodeIndex0;
                                        }
                                    }
                                }
                            }
                            else if (c1.a <= 0.1f && c2.a <= 0.1f
                                && nodeIndexLayer[x1, y1] == nodeIndexLayer[x0, y0])
                            {
                                continue;
                            }
                            else if (c1.a <= 0.1f && c2.a <= 0.1f
                                && nodeIndexLayer[x1, y1] != nodeIndexLayer[x0, y0])
                            {
                                int nodeIndex0 = nodeIndexLayer[x0, y0];
                                int nodeIndex1 = nodeIndexLayer[x1, y1];
                                for (int m = 0; m < graph.Count; m++)
                                {
                                    if (graph[m].nodeIndex == nodeIndex0)
                                    {
                                        for (int n = 0; n < graph.Count; n++)
                                        {
                                            if (graph[n].nodeIndex == nodeIndex1)
                                            {
                                                foreach (int tempIndex in graph[n].connections)
                                                {
                                                    graph[m].connections.Add(tempIndex);
                                                }
                                                graph.RemoveAt(n);
                                                break;
                                            }
                                        }
                                        break;
                                    }
                                }
                                for (int m = 0; m < graph.Count; m++)
                                {
                                    if (graph[m].connections.Contains(nodeIndex1) == true)
                                    {
                                        graph[m].connections.Remove(nodeIndex1);
                                        graph[m].connections.Add(nodeIndex0);
                                    }
                                }
                                for (int x = 0; x < ogTex.width; x++)
                                {
                                    for (int y = 0; y < ogTex.height; y++)
                                    {
                                        if (nodeIndexLayer[x, y] == nodeIndex1)
                                        {
                                            nodeIndexLayer[x, y] = nodeIndex0;
                                        }
                                    }
                                }
                            }
                            continue;
                        }
                        else if (Vector3.Distance(
                            new Vector3(c1.r, c1.g, c1.b),
                            new Vector3(c2.r, c2.g, c2.b))
                            <= colorTolerance && c1.a > 0.1f && c2.a > 0.1f)
                        {
                            // colors are similar enough, they belong to the same region
                            nodeIndexLayer[x1, y1] = nodeIndexLayer[x0, y0];
                            layerVisited[x1, y1] = true;
                            listOfPoints.Add(new Vector2(x1, y1));
                        }
                        else if (c1.a <= 0.1f && c2.a <= 0.1f)
                        {
                            // colors are similar enough, they belong to the same region
                            nodeIndexLayer[x1, y1] = nodeIndexLayer[x0, y0];
                            layerVisited[x1, y1] = true;
                            listOfPoints.Add(new Vector2(x1, y1));
                        }
                        else
                        {
                            // colors are different enough, they must belong to different regions
                            graphIndex++;
                            ConnectedGraph newNode2 = new ConnectedGraph();
                            newNode2.nodeIndex = graphIndex;
                            newNode2.connections = new HashSet<int>();
                            newNode2.connections.Add(nodeIndexLayer[x0, y0]);
                            graph.Add(newNode2);
                            for (int m = 0; m < graph.Count; m++)
                            {
                                if (graph[m].nodeIndex == nodeIndexLayer[x0, y0])
                                {
                                    graph[m].connections.Add(graphIndex);
                                    break;
                                }
                            }
                            nodeIndexLayer[x1, y1] = graphIndex;
                            layerVisited[x1, y1] = true;
                            listOfPoints.Add(new Vector2(x1, y1));
                        }
                    }
                }
            }
        }

        // 4-color mapping algorithm
        graph.Sort((x, y) => x.connections.Count.CompareTo(y.connections.Count));
        for (int i = graph.Count - 1; i >= 0; i--)
        {
            List<int> possibleColors = new List<int>();
            possibleColors.Add(1); possibleColors.Add(2); possibleColors.Add(3); possibleColors.Add(4);
            foreach (int node in graph[i].connections)
            {
                for (int j = 0; j < graph.Count; j++)
                {
                    if (node == graph[j].nodeIndex)
                    {
                        if (graph[j].colorIndex != 0)
                        {
                            possibleColors.Remove(graph[j].colorIndex);
                        }
                    }
                }
            }
            if (possibleColors.Count <= 0)
            {
                Debug.Log("... something went wrong in calculating 4-color mapping of regions...");
                graph[i].colorIndex = 1;
            }
            else
            {
                graph[i].colorIndex = possibleColors[0];
            }
        }

        for (int i = 0; i < ogTex.width; i++)
        {
            for (int j = 0; j < ogTex.height; j++)
            {
                int nodeIndex = nodeIndexLayer[i, j];
                returnLayer[i, j] = 0f;
                for (int k = 0; k < graph.Count; k++)
                {
                    if (graph[k].nodeIndex == nodeIndex)
                    {
                        returnLayer[i, j] = (float)graph[k].colorIndex / 4.0f;
                        if (graph[k].colorIndex == 0)
                        {
                            Debug.Log("...wait a minute... colorIndex not assigned...");
                        }
                    }
                }
            }
        }

        return returnLayer;
    }

    private float[,] CalculateRegionDistanceField(float[,] regionMap, float region)
    {
        /* Calculate Signed Distance Field out from boundaries, where boundary = 0.5;
            Let max range of DF be 0.25f of width of image for now (but should be a parameter than can be changed later).
            Convert down to [0.0, 1.0] at the end. 
         */

        int width = regionMap.GetLength(0);
        int height = regionMap.GetLength(1);
        float[,] returnLayer = new float[width, height];
        Vector2[,] nearestOutline = new Vector2[width, height];

        float targetRegion = region / 4.0f;
        float maxDF = width * 0.25f;

        List<Vector2> listOfPoints = new List<Vector2>();
        for (int i = 0; i < width; i++)
        {
            for (int j = 0; j < height; j++)
            {
                returnLayer[i, j] = -1;
            }
        }
        for (int i = 1; i < width - 1; i++)
        {
            for (int j = 1; j < height - 1; j++)
            {
                if (regionMap[i, j] == targetRegion)
                {
                    if (regionMap[i + 1, j] != targetRegion)
                    {
                        listOfPoints.Add(new Vector2(i, j));
                        nearestOutline[i, j] = (new Vector2(i, j) + new Vector2(i + 1, j)) * 0.5f;
                        returnLayer[i, j] = (maxDF * 0.5f) + (Vector2.Distance(new Vector2(i, j), nearestOutline[i, j]));
                        returnLayer[i, j] = returnLayer[i, j] / maxDF;
                    }
                    else if (regionMap[i - 1, j] != targetRegion)
                    {
                        listOfPoints.Add(new Vector2(i, j));
                        nearestOutline[i, j] = (new Vector2(i, j) + new Vector2(i - 1, j)) * 0.5f;
                        returnLayer[i, j] = (maxDF * 0.5f) + (Vector2.Distance(new Vector2(i, j), nearestOutline[i, j]));
                        returnLayer[i, j] = returnLayer[i, j] / maxDF;
                    }
                    else if (regionMap[i, j + 1] != targetRegion || regionMap[i, j - 1] != targetRegion)
                    {
                        listOfPoints.Add(new Vector2(i, j));
                        nearestOutline[i, j] = (new Vector2(i, j) + new Vector2(i, j + 1)) * 0.5f;
                        returnLayer[i, j] = (maxDF * 0.5f) + (Vector2.Distance(new Vector2(i, j), nearestOutline[i, j]));
                        returnLayer[i, j] = returnLayer[i, j] / maxDF;
                    }
                    else if (regionMap[i, j - 1] != targetRegion)
                    {
                        listOfPoints.Add(new Vector2(i, j));
                        nearestOutline[i, j] = (new Vector2(i, j) + new Vector2(i, j - 1)) * 0.5f;
                        returnLayer[i, j] = (maxDF * 0.5f) + (Vector2.Distance(new Vector2(i, j), nearestOutline[i, j]));
                        returnLayer[i, j] = returnLayer[i, j] / maxDF;
                    }
                }
            }
        }
        int distance = 0;
        while (listOfPoints.Count > 0 && distance < width)
        {
            // start from end to the front, pop off point and set it's neighbor cells to 
            distance++;
            int listIndex = listOfPoints.Count - 1;
            for (int i = listIndex; i >= 0; i--)
            {
                Vector2 point = listOfPoints[i];
                listOfPoints.RemoveAt(i);
                Vector2[] pNeigh = new Vector2[4];
                pNeigh[0] = point + new Vector2(1, 0);
                pNeigh[1] = point + new Vector2(-1, 0);
                pNeigh[2] = point + new Vector2(0, 1);
                pNeigh[3] = point + new Vector2(0, -1);
                for (int j = 0; j < pNeigh.Length; j++)
                {
                    int x1 = (int)pNeigh[j].x; int y1 = (int)pNeigh[j].y;
                    if (x1 < 0 || x1 >= width || y1 < 0 || y1 >= height)
                    {
                        continue;
                    }
                    if (returnLayer[x1, y1] == -1f)
                    {
                        nearestOutline[x1, y1] = nearestOutline[(int)point.x, (int)point.y];
                        float dis = Vector2.Distance(nearestOutline[(int)point.x, (int)point.y], pNeigh[j]);
                        listOfPoints.Add(pNeigh[j]);
                        if (regionMap[x1, y1] == targetRegion)
                        {
                            returnLayer[x1, y1] = Mathf.Min(1f, ((maxDF * 0.5f) + dis) / maxDF);
                        }
                        else
                        {
                            returnLayer[x1, y1] = Mathf.Max(0f, ((maxDF * 0.5f) - dis) / maxDF);
                        }
                    }
                    else
                    {
                        float dis = Vector2.Distance(nearestOutline[(int)point.x, (int)point.y], pNeigh[j]);
                        float finalDis1 = Mathf.Max(0f, ((maxDF * 0.5f) - (dis)) / maxDF);
                        finalDis1 = Mathf.Min(1f, finalDis1);
                        float finalDis2 = Mathf.Max(0f, ((maxDF * 0.5f) + (dis)) / maxDF);
                        finalDis2 = Mathf.Min(1f, finalDis2);
                        if (finalDis1 > returnLayer[x1, y1] && regionMap[x1, y1] != targetRegion)
                        {
                            nearestOutline[x1, y1] = nearestOutline[(int)point.x, (int)point.y];
                            returnLayer[x1, y1] = finalDis1;
                            listOfPoints.Add(pNeigh[j]);
                        }
                        else if (finalDis2 < returnLayer[x1, y1] && regionMap[x1, y1] == targetRegion)
                        {
                            nearestOutline[x1, y1] = nearestOutline[(int)point.x, (int)point.y];
                            returnLayer[x1, y1] = finalDis2;
                            listOfPoints.Add(pNeigh[j]);
                        }
                    }

                }
            }
        }

        return returnLayer;
    }

    Texture2D ReduceTextureResolution(Texture2D ogTex, int mode, int resTarget)
    {
        // mode = 0 ( take average of neighbourhood ) , mode = 1 ( take average, but keep 1.0 as 1.0 ), mode = 2 ( take middle value as sample )

        int res = resTarget;
        Texture2D newTex = new Texture2D(res, res);
        // first, do NOT simply blend pixels where pixel = 1.0, otherwise we're losing important information.
        if (mode == 0)
        {
            for (int x = 0; x < res; x++)
            {
                for (int y = 0; y < res; y++)
                {
                    Vector4 totalColor = Vector4.zero;
                    float totalColorCount = 0;
                    int size = (int)Mathf.Floor(ogTex.width / res);
                    for (int i = x * size; i < Mathf.Min((x * size) + size, ogTex.width); i++)
                    {
                        for (int j = y * size; j < Mathf.Min((y * size) + size, ogTex.height); j++)
                        {
                            totalColorCount++;
                            totalColor += new Vector4(ogTex.GetPixel(i, j).r, ogTex.GetPixel(i, j).g, ogTex.GetPixel(i, j).b, ogTex.GetPixel(i, j).a);
                        }
                    }
                    totalColor = totalColor / totalColorCount;
                    newTex.SetPixel(x, y, new Color(totalColor.x, totalColor.y, totalColor.z, totalColor.w));
                }
            }
        }
        else if (mode == 1)
        {
            for (int x = 0; x < res; x++)
            {
                for (int y = 0; y < res; y++)
                {
                    Vector4 totalColor = Vector4.zero;
                    float totalColorCount = 0;
                    int size = (int)Mathf.Floor(ogTex.width / res);
                    bool maxIsPresentR = false;
                    bool maxIsPresentG = false;
                    bool maxIsPresentB = false;
                    bool maxIsPresentA = false;
                    for (int i = x * size; i < Mathf.Min((x * size) + size, ogTex.width); i++)
                    {
                        for (int j = y * size; j < Mathf.Min((y * size) + size, ogTex.height); j++)
                        {
                            totalColorCount++;
                            totalColor += new Vector4(ogTex.GetPixel(i, j).r, ogTex.GetPixel(i, j).g, ogTex.GetPixel(i, j).b, ogTex.GetPixel(i, j).a);
                            if (ogTex.GetPixel(i, j).r == 1.0)
                            {
                                maxIsPresentR = true;
                            }
                            if (ogTex.GetPixel(i, j).g == 1.0)
                            {
                                maxIsPresentG = true;
                            }
                            if (ogTex.GetPixel(i, j).b == 1.0)
                            {
                                maxIsPresentB = true;
                            }
                            if (ogTex.GetPixel(i, j).a == 1.0)
                            {
                                maxIsPresentA = true;
                            }
                        }
                    }
                    totalColor = totalColor / totalColorCount;
                    if (maxIsPresentR == true)
                        totalColor = new Vector4(1.0f, totalColor.y, totalColor.z, totalColor.w);
                    if (maxIsPresentG == true)
                        totalColor = new Vector4(totalColor.x, 1.0f, totalColor.z, totalColor.w);
                    if (maxIsPresentB == true)
                        totalColor = new Vector4(totalColor.x, totalColor.y, 1.0f, totalColor.w);
                    if (maxIsPresentA == true)
                        totalColor = new Vector4(totalColor.x, totalColor.y, totalColor.z, 1.0f);
                    newTex.SetPixel(x, y, new Color(totalColor.x, totalColor.y, totalColor.z, totalColor.w));
                }
            }
            newTex.Apply();
            Color[,] tempNewTex = new Color[newTex.width, newTex.height];
            for (int x = 0; x < res; x++)
            {
                for (int y = 0; y < res; y++)
                {
                    Color pix = newTex.GetPixel(x, y);
                    tempNewTex[x, y] = pix;
                    Color pix1 = newTex.GetPixel(Mathf.Max(0, x - 1), y);
                    Color pix2 = newTex.GetPixel(Mathf.Min(newTex.width - 1, x + 1), y);
                    Color pix3 = newTex.GetPixel(x, Mathf.Max(0, y - 1));
                    Color pix4 = newTex.GetPixel(x, Mathf.Min(newTex.height - 1, y + 1));
                    if (pix1.r == 1.0f || pix2.r == 1.0f || pix3.r == 1.0f || pix4.r == 1.0f)
                    {
                        float r = pix.r;
                        if (r != 1.0f)
                        {
                            r = (pix1.r + pix2.r + pix3.r + pix4.r) / 4.0f;
                            if (pix.r < 0.5f && r >= 0.5f)
                            {
                                r = (r + pix.r) * 0.5f;
                            }
                            else if (pix.r >= 0.5f && r < 0.5)
                            {
                                r = (r + pix.r) * 0.5f;
                            }
                            tempNewTex[x, y] = new Color(r, pix.g, pix.b, pix.a);
                        }
                    }
                    if (pix.g == 1.0f || pix2.g == 1.0f || pix3.g == 1.0f || pix4.g == 1.0f)
                    {
                        float r = pix.g;
                        if (r != 1.0f)
                        {
                            r = (pix1.g + pix2.g + pix3.g + pix4.g) / 4.0f;
                            tempNewTex[x, y] = new Color(pix.r, r, pix.b, pix.a);
                        }
                    }
                    if (pix.b == 1.0f || pix2.b == 1.0f || pix3.b == 1.0f || pix4.b == 1.0f)
                    {
                        float r = pix.b;
                        if (r != 1.0f)
                        {
                            r = (pix1.b + pix2.b + pix3.b + pix4.b) / 4.0f;
                            tempNewTex[x, y] = new Color(pix.r, pix.g, r, pix.a);
                        }
                    }
                    if (pix.a == 1.0f || pix2.a == 1.0f || pix3.a == 1.0f || pix4.a == 1.0f)
                    {
                        float r = pix.a;
                        if (r != 1.0f)
                        {
                            r = (pix1.a + pix2.a + pix3.a + pix4.a) / 4.0f;
                            tempNewTex[x, y] = new Color(pix.r, pix.g, pix.b, r);
                        }
                    }
                }
            }
            for (int x = 0; x < res; x++)
            {
                for (int y = 0; y < res; y++)
                {
                    newTex.SetPixel(x, y, tempNewTex[x, y]);
                }
            }
        }
        else if (mode == 2)
        {
            for (int x = 0; x < res; x++)
            {
                for (int y = 0; y < res; y++)
                {
                    Vector4 totalColor = Vector4.zero;
                    int size = (int)Mathf.Floor(ogTex.width / res);
                    int i = x * size;
                    int j = y * size;
                    totalColor += new Vector4(ogTex.GetPixel(i, j).r, ogTex.GetPixel(i, j).g, ogTex.GetPixel(i, j).b, ogTex.GetPixel(i, j).a);
                    newTex.SetPixel(x, y, new Color(totalColor.x, totalColor.y, totalColor.z, totalColor.w));
                }
            }
        }
        newTex.Apply();

        return newTex;
    }

    private Texture2D Calculate4ColorMappingLutToTexture(Texture2D ogTex, Texture2D bgTex, Texture2D colorMapTex)
    {
        /* Takes texture as input, 
         * outputs same texture with ColorMap (R,G,B,A values, applied to different rows) applied to 1 of its channels.
         */

        Texture2D returnTex = new Texture2D(ogTex.width, ogTex.height, TextureFormat.RGBA32, false, true);
        float[,] newLayer = new float[ogTex.width, ogTex.height];

        // Calculate LUT for Blend background colours, store in layer here.
        newLayer = Calculate4ColorMappingLut(bgTex, colorMapTex);

        for (int x = 0; x < ogTex.width; x++)
        {
            for (int y = 0; y < ogTex.height; y++)
            {
                Color ogColor = ogTex.GetPixel(x, y);
                returnTex.SetPixel(x, y, new Color(ogColor.r, newLayer[x, y], ogColor.b, ogColor.a));
            }
        }
        returnTex.Apply();

        return returnTex;
    }

    private float[,] Calculate4ColorMappingLut(Texture2D ogTex, Texture2D colorMapTex)
    {
        /*  Idea: for each of the 4 DF's for color background, assign a value that maps to this layer, which stores the color.
         *      * 1st 1/4 rows: R, 2nd 1/4 rows: G, 3rd 1/4 rows: B, 4th 1/4 rows: A (not used)
         *      * e.g. for 64x64 texture, mapping to color 5 = RGBA = ( [5 % width, Mathf.Floor(5 / width)], [5 % width, Mathf.Floor(5 / width) + (1/4*height)], ...)
         
         *      * ... need the ColorBlend DF first to calculate this...       
         */
        float[,] returnLayer = new float[ogTex.width, ogTex.height];


        List<Color> listOfColors = new List<Color>();
        float[][,] colorMapTexChannel = new float[4][,];//GetColorChannel(colorMapTex, 1);
        for (int i = 0; i < 4; i++)
        {
            colorMapTexChannel[i] = GetColorChannel(colorMapTex, i + 1);

            // get peaks in the channel
            List<Vector2> channelPeaks = new List<Vector2>();
            for (int x = 1; x < ogTex.width - 1; x++)
            {
                for (int y = 1; y < ogTex.height - 1; y++)
                {
                    if (colorMapTexChannel[i][x, y] > 0
                        && colorMapTexChannel[i][x, y] >= colorMapTexChannel[i][x + 1, y]
                        && colorMapTexChannel[i][x, y] >= colorMapTexChannel[i][x - 1, y]
                        && colorMapTexChannel[i][x, y] >= colorMapTexChannel[i][x, y + 1]
                        && colorMapTexChannel[i][x, y] >= colorMapTexChannel[i][x, y - 1])
                    {
                        channelPeaks.Add(new Vector2(x, y));
                    }
                }
            }
            for (int j = 0; j < channelPeaks.Count; j++)
            {
                Color newColor = ogTex.GetPixel((int)channelPeaks[j].x, (int)channelPeaks[j].y);
                bool colorInList = false;
                for (int k = listOfColors.Count - 1; k >= 0; k--)
                {
                    if (ColorEquals(listOfColors[k], newColor) == true)
                    {
                        colorInList = true;
                        break;
                    }
                }
                if (colorInList == false)
                {
                    listOfColors.Add(newColor);
                }
            }
        }
        // add colors to texture layer. Only works if we don't run out of space: e.g. 32x32 texture / 4 = 256 colors max
        if (listOfColors.Count > (ogTex.width * ogTex.height) / 4.0f)
        {
            //Debug.Log("ERROR: Too many colors in this texture, can't store LUT in a single color channel.");
            return returnLayer;
        }
        if (listOfColors.Count > 256)
        {
            //Debug.Log("ERROR: may not be able to store color indexes in 256-sized color channel LUT.");
        }
        for (int i = 0; i < ogTex.width; i++)
        {
            for (int j = 0; j < ogTex.height; j++)
            {
                returnLayer[i, j] = 0f;
            }
        }
        for (int i = 0; i < listOfColors.Count; i++)
        {
            returnLayer[i % ogTex.width, Mathf.FloorToInt(i / ogTex.width) + (int)Mathf.FloorToInt(0.0f * ogTex.height)] = listOfColors[i].r;
            returnLayer[i % ogTex.width, Mathf.FloorToInt(i / ogTex.width) + (int)Mathf.FloorToInt(0.25f * ogTex.height)] = listOfColors[i].g;
            returnLayer[i % ogTex.width, Mathf.FloorToInt(i / ogTex.width) + (int)Mathf.FloorToInt(0.5f * ogTex.height)] = listOfColors[i].b;
            returnLayer[i % ogTex.width, Mathf.FloorToInt(i / ogTex.width) + (int)Mathf.FloorToInt(0.75f * ogTex.height)] = listOfColors[i].a;
        }

        return returnLayer;
    }

    float[,] GetColorChannel(Texture2D ogTex, int a)
    {
        float[,] returnTex = new float[ogTex.width, ogTex.height];

        for (int i = 0; i < ogTex.width; i++)
        {
            for (int j = 0; j < ogTex.height; j++)
            {
                if (a == 1)
                {
                    returnTex[i, j] = ogTex.GetPixel(i, j).r;
                }
                else if (a == 2)
                {
                    returnTex[i, j] = ogTex.GetPixel(i, j).g;
                }
                else if (a == 3)
                {
                    returnTex[i, j] = ogTex.GetPixel(i, j).b;
                }
                else if (a == 4)
                {
                    returnTex[i, j] = ogTex.GetPixel(i, j).a;
                }
            }
        }

        return returnTex;
    }

    bool ColorEquals(Color c1, Color c2)
    {
        bool returnValue = false;
        float colorDiff = 0.04f;    //0.01f;        // difference between 1 and 2 in 256 space is 0.0039

        if (Mathf.Abs(c1.r - c2.r) < colorDiff && Mathf.Abs(c1.g - c2.g) < colorDiff && Mathf.Abs(c1.b - c2.b) < colorDiff && Mathf.Abs(c1.a - c2.a) < colorDiff)
        {
            returnValue = true;
        }

        return returnValue;
    }

    bool ColorEquals(Color c1, float[] c2)
    {
        bool returnValue = false;
        float colorDiff = 0.04f;    //0.01f;        // difference between 1 and 2 in 256 space is 0.0039

        if (Mathf.Abs(c1.r - c2[0]) < colorDiff && Mathf.Abs(c1.g - c2[1]) < colorDiff && Mathf.Abs(c1.b - c2[2]) < colorDiff && Mathf.Abs(c1.a - c2[3]) < colorDiff)
        {
            returnValue = true;
        }

        return returnValue;
    }

    public class ConnectedGraph
    {
        public int nodeIndex = 0;
        public HashSet<int> connections;
        public int colorIndex = 0;
    }

    public class Quantizer
    {
        // Associated with "FlattenColors()" function
        int MAX_DEPTH = 8;
        LevelNode[] levels;
        Node root;

        class LevelNode
        {
            public List<Node> nodes;
        }

        public Quantizer()
        {
            levels = new LevelNode[MAX_DEPTH];
            for (int i = 0; i < levels.Length; i++)
            {
                levels[i] = new LevelNode();
                levels[i].nodes = new List<Node>();
            }
            this.root = new Node(0, this);

        }

        public void AddColor(Color c)
        {
            this.root.AddColor(c, 0, this);
        }

        public List<Color> MakePalette(int colorCount)
        {
            List<Color> palette = new List<Color>();
            int paletteIndex = 0;
            int leafCount = this.GetLeafNodes().Count;
            for (int level = MAX_DEPTH - 1; level > -1; level--)
            {
                if (this.levels[level] != null)
                {
                    foreach (Node node in this.levels[level].nodes)
                    {
                        leafCount -= node.RemoveLeaves();
                        if (leafCount <= colorCount)
                            break;
                    }
                    if (leafCount <= colorCount)
                        break;
                    this.levels[level].nodes.Clear();
                }
            }
            foreach (Node node in this.GetLeafNodes())
            {
                if (paletteIndex >= colorCount) break;
                if (node.IsLeaf())
                {
                    palette.Add(node.GetColor());
                }
                node.paletteIndex = paletteIndex;
                paletteIndex++;
            }
            return palette;
        }

        List<Node> GetLeafNodes()
        {
            return this.root.LeafNodes();
        }

        public void AddLevelNode(int level, Node node)
        {
            this.levels[level].nodes.Add(node);
        }

        public int GetPaletteIndex(Color c)
        {
            return this.root.GetPaletteIndex(c, 0);
        }
    }

    public class Node
    {
        // Associated with "FlattenColors()" function
        List<Color> c;
        int pixelCount = 0;
        public int paletteIndex = 0;
        List<Node> children;
        int MAX_DEPTH = 8;

        public Node(int level, Quantizer parent)
        {
            c = new List<Color>();
            this.c.Add(new Color(0, 0, 0));
            this.pixelCount = 0;
            this.paletteIndex = 0;
            this.children = new List<Node>();
            if (level < MAX_DEPTH - 1)
            {
                parent.AddLevelNode(level, this);
            }
            for (int i = 0; i < MAX_DEPTH; i++)
            {
                this.children.Add(null);
            }
        }

        public bool IsLeaf()
        {
            return this.pixelCount > 0;
        }

        public List<Node> LeafNodes()
        {
            List<Node> returnNodes = new List<Node>();
            foreach (Node node in this.children)
            {
                if (node == null) continue;
                if (node.IsLeaf() == true)
                {
                    returnNodes.Add(node);
                }
                else
                {
                    returnNodes.AddRange(node.LeafNodes());
                }
            }
            return returnNodes;
        }

        public void AddColor(Color c, int level, Quantizer parent)
        {
            if (level >= MAX_DEPTH)
            {
                this.c.Add(c);
                this.pixelCount++;
                return;
            }
            int index = GetColorIndex(c, level);
            if (this.children[index] == null)
            {
                this.children[index] = new Node(level, parent);
            }
            this.children[index].AddColor(c, level + 1, parent);
        }

        public int GetPaletteIndex(Color c, int level)
        {
            if (this.IsLeaf())
                return this.paletteIndex;
            int index = GetColorIndex(c, level);
            if (this.children.Count > index && this.children[index] != null)
            {
                return this.children[index].GetPaletteIndex(c, level + 1);
            }
            else
            {
                foreach (Node node in this.children)
                {
                    if (node != null)
                    {
                        return node.GetPaletteIndex(c, level + 1);
                    }
                }
                return -1;
            }
        }

        public int RemoveLeaves()
        {
            int result = 0;
            foreach (Node node in this.children)
            {
                if (node == null)
                    continue;
                this.c.AddRange(node.c);
                this.pixelCount += node.pixelCount;
                result++;
            }
            this.children.Clear();
            this.children = new List<Node>();
            for (int i = 0; i < MAX_DEPTH; i++)
            {
                this.children.Add(null);
            }
            return result - 1;
        }

        public Color GetColor()
        {
            float r = 0, g = 0, b = 0;
            for (int i = 0; i < c.Count; i++)
            {
                r += c[i].r;
                g += c[i].g;
                b += c[i].b;
            }
            if (pixelCount > 0)
                return new Color(r / pixelCount, g / pixelCount, b / pixelCount);
            else
                return new Color(r, g, b);
        }

    }

    public static int GetColorIndex(Color c, int level)
    {
        // Function to separate color into binary, correlates nicely with 8-bit / 8-branch octree.
        int returnValue = 0;

        int mask = 0b10000000 >> level;
        if (((int)(c.r * 255) & mask) != 0)
            returnValue = returnValue | 0b100;
        if (((int)(c.g * 255) & mask) != 0)
            returnValue = returnValue | 0b010;
        if (((int)(c.b * 255) & mask) != 0)
            returnValue = returnValue | 0b001;
        return returnValue;
    }



}


