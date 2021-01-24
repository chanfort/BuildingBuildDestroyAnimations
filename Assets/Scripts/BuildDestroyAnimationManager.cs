using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR	
#if ! UNITY_WEBPLAYER
using System.IO;
#endif 		
#endif

public class BuildDestroyAnimationManager : MonoBehaviour
{
    public int mode = 0;

    // 0 - build
    // 1 - destroy
    // 2 = build and destroy

    public bool debugMode = false;

    Mesh originalMesh;
    Mesh originalMeshRuntime;

    void Start()
    {
        MeshFilter mf = GetComponent<MeshFilter>();
        originalMesh = mf.mesh;
        originalMeshRuntime = CopyMesh(originalMesh);
        mf.mesh = originalMeshRuntime;

        Initialize();

        if ((mode == 0) || (mode == 2))
        {
            StartCoroutine(BuildSequence());
        }
        if (mode == 1)
        {
            DestroyMesh();
        }
    }

    KDTree kd;

    void Initialize()
    {
        Mesh msh = originalMeshRuntime;

        o_vertices = msh.vertices;
        o_normals = msh.normals;
        o_uv = msh.uv;
        o_triangles = msh.triangles;

        for (int i = 0; i < o_vertices.Length; i++)
        {
            o_vertices[i] = o_vertices[i] + 0.00001f * Random.insideUnitSphere;
        }

        kd = KDTree.MakeFromPoints(o_vertices);
        o_neighbours = new List<List<int>>();

        for (int i = 0; i < o_vertices.Length; i++)
        {
            o_neighbours.Add(new List<int>());

            int[] neighs = kd.FindNearestsK(o_vertices[i] + 0.00001f * Random.insideUnitSphere, 5);

            for (int j = 0; j < neighs.Length; j++)
            {
                int id = neighs[j];

                if (id >= 0)
                {
                    if (id < o_vertices.Length)
                    {
                        float r = (o_vertices[i] - o_vertices[id]).magnitude;

                        if (r < 0.0001f)
                        {
                            o_neighbours[i].Add(id);
                        }
                    }
                }
            }
        }

        if (debugMode)
        {
            Debug.Log(o_vertices.Length);
        }

        o_indices = new List<int[]>();
        o_topology = new List<MeshTopology>();

        for (int i = 0; i < msh.subMeshCount; i++)
        {
            o_indices.Add(msh.GetIndices(i));
            o_topology.Add(msh.GetTopology(i));
        }

        if (msh != null)
        {
            FindLinkedVertex(msh);
        }
    }

    Vector3[] o_vertices;
    Vector3[] o_normals;
    Vector2[] o_uv;
    int[] o_triangles;
    List<int[]> o_indices;
    List<MeshTopology> o_topology;
    List<List<int>> o_neighbours;

    int[] o_clusterId;
    int o_nTri = 0;
    int[,] o_triangles3;

    IEnumerator BuildSequence()
    {
        Mesh msh = originalMeshRuntime;
        int iBuildPrinting = 0;

        for (int i = 0; i < nClustersTot; i++)
        {
            iClustCut++;

            if (debugMode)
            {
                Debug.Log(iClustCut);
            }

            SetPassMaskBelow(iClustCut);
            SetMesh(msh);

            if ((fileWriteMode == 0) || (fileWriteMode == 2))
            {

                int totId = i;
                int nFrac = nClustersTot / (numBuildFrames - 1);

                if (totId % nFrac == 0)
                {
                    iBuildPrinting++;
                    WriteCurrentBuildIntoFile(iBuildPrinting);
                }
            }

            yield return new WaitForSeconds(0.1f);
        }

        if (mode == 2)
        {
            DestroyMesh();
        }

        yield return new WaitForSeconds(0.1f);
    }

    int iClustCut = 10;
    int nClustersTot = 0;

    void FindLinkedVertex(Mesh msh)
    {
        Vector3[] vertices = o_vertices;
        int[] trianglesOrig = o_triangles;

        int[,] triangles = new int[trianglesOrig.Length / 3, 3];
        int[] clusterId = new int[vertices.Length];


        // Finding linked vertex groups	- clusters

        for (int i = 0; i < clusterId.Length; i++)
        {
            clusterId[i] = 0;
        }

        int j1 = 0;
        int nTri = 0;

        for (int i = 0; i < trianglesOrig.Length; i++)
        {
            triangles[nTri, j1] = trianglesOrig[i];
            j1++;

            if (j1 > 2)
            {
                nTri++;
                j1 = 0;
            }
        }

        int nClusters = 0;

        for (int i = 0; i < nTri; i++)
        {
            int clustId = 0;

            for (int j = 0; j < 3; j++)
            {
                int v = triangles[i, j];

                if (clustId == 0)
                {
                    if (clusterId[v] != 0)
                    {
                        clustId = clusterId[v];
                    }
                }
            }

            if (clustId != 0)
            {
                for (int j = 0; j < 3; j++)
                {
                    int v = triangles[i, j];
                    clusterId[v] = clustId;
                }
            }
            else
            {
                nClusters++;

                for (int j = 0; j < 3; j++)
                {
                    int v = triangles[i, j];
                    clusterId[v] = nClusters;
                }
            }
        }

        if (debugMode)
        {
            int nUnclusteredVerts = 0;

            for (int i = 0; i < vertices.Length; i++)
            {
                if (clusterId[i] == 0)
                {
                    nUnclusteredVerts++;
                }
            }

            Debug.Log("nUnclusteredVerts " + nUnclusteredVerts);
        }

        int nClustersPrev = nClusters * 2;
        int imerge = 0;

        // Merging clusters	
        List<List<int>> clusterVerticesList;

        while ((nClustersPrev > nClusters) && (imerge < 25))
        {

            imerge++;
            nClustersPrev = nClusters;

            if (nClusters > 0)
            {
                // merging by neighbours		
                for (int i = 0; i < vertices.Length; i++)
                {
                    for (int j = 0; j < o_neighbours[i].Count; j++)
                    {
                        int id = o_neighbours[i][j];
                        if (clusterId[i] != 0)
                        {
                            if (clusterId[id] != 0)
                            {
                                if (clusterId[i] != clusterId[id])
                                {
                                    if (clusterId[i] < clusterId[id])
                                    {
                                        clusterId[id] = clusterId[i];
                                    }
                                    else if (clusterId[i] > clusterId[id])
                                    {
                                        clusterId[i] = clusterId[id];
                                    }
                                }
                            }
                        }
                    }
                }

                // merging by triangles
                int[] clusterRepeat = new int[nClusters];

                for (int i = 0; i < nTri; i++)
                {

                    for (int j = 0; j < 3; j++)
                    {
                        int v = triangles[i, j];
                        int clustId = clusterId[v];

                        if (clustId > 0)
                        {
                            clusterRepeat[clustId - 1] = 0;
                        }
                    }

                    int nmax = 0;
                    int cmax = 0;

                    for (int j = 0; j < 3; j++)
                    {
                        int v = triangles[i, j];
                        int clustId = clusterId[v];

                        if (clustId > 0)
                        {
                            clusterRepeat[clustId - 1] = clusterRepeat[clustId - 1] + 1;

                            if (clusterRepeat[clustId - 1] > nmax)
                            {
                                nmax = clusterRepeat[clustId - 1];
                                cmax = clustId;
                            }
                        }
                    }

                    if (nmax < 3)
                    {
                        for (int j = 0; j < 3; j++)
                        {
                            int v = triangles[i, j];
                            clusterId[v] = cmax;
                        }
                    }

                }

                clusterRepeat = new int[nClusters];

                for (int i = 0; i < clusterRepeat.Length; i++)
                {
                    clusterRepeat[i] = 0;
                }

                for (int i = 0; i < clusterId.Length; i++)
                {
                    int j = clusterId[i];

                    if (j > 0)
                    {
                        clusterRepeat[j - 1] = clusterRepeat[j - 1] + 1;
                    }
                }

                int[] clusterRemap = new int[nClusters];

                for (int i = 0; i < clusterRemap.Length; i++)
                {
                    clusterRemap[i] = 0;
                }

                int nClusters2 = 0;

                for (int i = 0; i < clusterRepeat.Length; i++)
                {
                    if (clusterRepeat[i] > 0)
                    {
                        nClusters2++;
                        clusterRemap[i] = nClusters2;
                    }
                }

                for (int i = 0; i < clusterId.Length; i++)
                {
                    int j = clusterId[i];

                    if (j > 0)
                    {
                        clusterId[i] = clusterRemap[j - 1];
                    }
                }

                nClusters = nClusters2;
            }
        }

        if (debugMode)
        {
            Debug.Log("nClusters " + nClusters);
        }

        clusterVerticesList = new List<List<int>>();

        for (int i = 0; i < nClusters; i++)
        {
            clusterVerticesList.Add(new List<int>());
        }

        for (int i = 0; i < clusterId.Length; i++)
        {
            int j = clusterId[i];

            if (j > 0)
            {
                clusterVerticesList[j - 1].Add(i);
            }
        }

        // Sorting clusters by Y
        float[] ys = new float[nClusters];
        int axis = 2;

        float ymax = float.MaxValue;

        for (int i = 0; i < vertices.Length; i++)
        {
            if (vertices[i][axis] < ymax)
            {
                ymax = vertices[i][axis];
            }
        }

        nClustersTot = nClusters;

        for (int i = 0; i < nClusters; i++)
        {
            float ys1 = ymax;
            int n22 = 0;

            for (int j = 0; j < clusterVerticesList[i].Count; j++)
            {
                int k = clusterVerticesList[i][j];

                n22++;
                ys1 = ys1 + vertices[k][axis];
            }

            ys1 = ys1 / n22;
            ys[i] = ys1;
        }

        int[] sortIds = HeapSort(ys);

        for (int i = 0; i < clusterId.Length; i++)
        {
            clusterId[i] = 0;
        }

        for (int i = 0; i < nClusters; i++)
        {
            int isort = sortIds[i];

            for (int j = 0; j < clusterVerticesList[isort].Count; j++)
            {
                int k = clusterVerticesList[isort][j];

                clusterId[k] = i + 1;
            }
        }

        o_clusterId = clusterId;
        o_nTri = nTri;
        o_triangles3 = triangles;
    }

    int[] passMask;
    void SetPassMaskBelow(int clustVal)
    {
        passMask = new int[o_vertices.Length];
        int i3 = 0;

        for (int i = 0; i < o_vertices.Length; i++)
        {
            passMask[i] = -1;

            if (o_clusterId[i] < clustVal)
            {
                passMask[i] = i3;
                i3++;
            }
        }
    }

    void SetPassMaskExclude(int clustVal)
    {
        int i3 = 0;

        for (int i = 0; i < o_vertices.Length; i++)
        {
            if (passMask[i] != -1)
            {
                if (o_clusterId[i] == clustVal)
                {
                    passMask[i] = -1;
                }
                else
                {
                    passMask[i] = i3;
                    i3++;
                }
            }
        }
    }


    void SetPassMaskAbove(int clustVal)
    {
        passMask = new int[o_vertices.Length];
        int i3 = 0;

        for (int i = 0; i < o_vertices.Length; i++)
        {
            passMask[i] = -1;

            if (o_clusterId[i] > clustVal)
            {
                passMask[i] = i3;
                i3++;
            }
        }
    }

    void SetPassMaskBetween(int min, int max)
    {
        passMask = new int[o_vertices.Length];
        int i3 = 0;

        for (int i = 0; i < o_vertices.Length; i++)
        {
            passMask[i] = -1;

            if (o_clusterId[i] > min)
            {
                if (o_clusterId[i] < max)
                {
                    passMask[i] = i3;
                    i3++;
                }
            }
        }
    }

    void SetMesh(Mesh newMesh)
    {
        // setting up new mesh

        List<Vector3> newVertices = new List<Vector3>();
        List<Vector3> newNormals = new List<Vector3>();
        List<Vector2> newUv = new List<Vector2>();

        for (int i = 0; i < o_vertices.Length; i++)
        {
            if (passMask[i] > -1)
            {
                newVertices.Add(o_vertices[i]);
                newNormals.Add(o_normals[i]);
                newUv.Add(o_uv[i]);
            }
        }

        List<int> newTriangles = new List<int>();

        for (int i = 0; i < o_nTri; i++)
        {
            bool pass = true;

            for (int j = 0; j < 3; j++)
            {
                int k = o_triangles3[i, j];

                if (passMask[k] <= -1)
                {
                    pass = false;
                }
            }

            if (pass)
            {
                for (int j = 0; j < 3; j++)
                {
                    int k = o_triangles3[i, j];
                    int newi = passMask[k];
                    newTriangles.Add(newi);
                }
            }
        }

        List<List<int>> newSubMeshIndices = new List<List<int>>();

        for (int i = 0; i < o_indices.Count; i++)
        {
            newSubMeshIndices.Add(new List<int>());

            for (int j = 0; j < o_indices[i].Length; j++)
            {
                if ((j % 3) == 0)
                {
                    int j2 = j;
                    bool pass = true;

                    for (int j3 = 0; j3 < 3; j3++)
                    {
                        int k = o_indices[i][j2];

                        if (passMask[k] <= -1)
                        {
                            pass = false;
                        }

                        j2 = j2 + 1;
                    }

                    if (pass)
                    {
                        j2 = j;

                        for (int j3 = 0; j3 < 3; j3++)
                        {
                            int k = o_indices[i][j2];
                            newSubMeshIndices[i].Add(passMask[k]);
                            j2 = j2 + 1;
                        }
                    }
                }
            }
        }

        newMesh.Clear();

        newMesh.vertices = newVertices.ToArray();
        newMesh.normals = newNormals.ToArray();
        newMesh.uv = newUv.ToArray();
        newMesh.triangles = newTriangles.ToArray();
        newMesh.subMeshCount = o_indices.Count;

        for (int i = 0; i < o_indices.Count; i++)
        {
            newMesh.SetIndices(newSubMeshIndices[i].ToArray(), o_topology[i], i);
        }

        newMesh.RecalculateBounds();
    }

    void DestroyMesh()
    {
        StartCoroutine(DestrAll());
    }

    bool randomiseDestructionOrder = true;

    IEnumerator DestrAll()
    {
        SetPassMaskBelow(nClustersTot + 1);
        maskOrig1 = new int[passMask.Length];

        int[] clustRemovals = new int[nClustersTot + 1];

        for (int i = 0; i < clustRemovals.Length; i++)
        {
            clustRemovals[i] = i;
        }

        if (randomiseDestructionOrder)
        {
            RandomiseArrayConverging(clustRemovals, clustRemovals.Length, 0, clustRemovals.Length - 5);
        }

        int iDestrPrinting = 0;

        for (int i = nClustersTot; i >= -nPiecesToKeep; i--)
        {
            if (i >= 0)
            {
                isDestroyMeshRunning = true;
                StartCoroutine(DestroyMesh(clustRemovals[i]));

                while (isDestroyMeshRunning)
                {
                    yield return new WaitForEndOfFrame();
                }
            }

            if ((i <= nClustersTot - nPiecesToKeep) || (i < 0))
            {
                if (destructionPieces.Count > 0)
                {
                    Mesh destMesh = destructionPiecesMesh[0];
                    destructionPiecesMesh.RemoveAt(0);

                    if (destMesh != null)
                    {
                        Destroy(destMesh);
                    }

                    GameObject dest = destructionPieces[0];
                    destructionPieces.RemoveAt(0);

                    Destroy(dest);
                }
            }

            if ((fileWriteMode == 1) || (fileWriteMode == 2))
            {
                int totId = (nClustersTot + nPiecesToKeep) - i;
                int nFrac = (nClustersTot + nPiecesToKeep) / numDestroyFrames;

                if (totId % nFrac == 0)
                {
                    List<GameObject> meshesToSave = new List<GameObject>();
                    List<Mesh> meshesToSaveM = new List<Mesh>();

                    meshesToSave.Add(this.gameObject);
                    meshesToSaveM.Add(originalMeshRuntime);

                    for (int i2 = 0; i2 < destructionPieces.Count; i2++)
                    {
                        meshesToSave.Add(destructionPieces[i2]);
                        meshesToSaveM.Add(destructionPiecesMesh[i2]);
                    }

                    iDestrPrinting++;
                    WriteCurrentDestroyIntoFile(meshesToSave, meshesToSaveM, iDestrPrinting);
                }
            }

            yield return new WaitForSeconds(0.2f);
        }

        yield return null;
    }

    List<GameObject> destructionPieces = new List<GameObject>();
    List<Mesh> destructionPiecesMesh = new List<Mesh>();
    int[] maskOrig1;
    public int nPiecesToKeep = 60;
    public float initialForceScaler = 0f;

    bool isDestroyMeshRunning = false;

    public bool reproduceError = true;
    
    IEnumerator DestroyMesh(int iDestr)
    {
        isDestroyMeshRunning = true;
        Mesh msh = new Mesh();

        SetPassMaskExclude(iDestr);

        CopyMask(maskOrig1, passMask);

        SetMesh(originalMeshRuntime);

        MeshCollider colHere = GetComponent<MeshCollider>();

        if (colHere != null)
        {
            Destroy(colHere);
            this.gameObject.AddComponent<MeshCollider>();
        }

        SetPassMaskBetween(iDestr - 1, iDestr + 1);
        SetMesh(msh);

        CopyMask(passMask, maskOrig1);

        if ((msh.vertices.Length > 0) && (msh.triangles.Length > 0))
        {
            GameObject go = new GameObject("newMesh");

            go.transform.position = transform.position;
            go.transform.rotation = transform.rotation;
            go.transform.localScale = transform.localScale;

            MeshFilter mf = go.AddComponent<MeshFilter>();

            mf.mesh = msh;

            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            Material[] materialsHere = GetComponent<MeshRenderer>().materials;
            mr.materials = materialsHere;

            if (reproduceError == false)
            {
                mr.enabled = false;
            }

            yield return new WaitForEndOfFrame();

            MeshCollider col = go.AddComponent<MeshCollider>();
            col.convex = true;
            col.sharedMesh = msh;

            yield return new WaitForEndOfFrame();

            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.maxDepenetrationVelocity = 0.1f;
            rb.drag = 0.05f;

            rb.AddForce(initialForceScaler * Random.insideUnitSphere, ForceMode.VelocityChange);

            destructionPieces.Add(go);
            destructionPiecesMesh.Add(msh);
        }

        yield return new WaitForEndOfFrame();

        isDestroyMeshRunning = false;
    }

    void RandomiseArrayConverging(int[] array, int nToRandomise, int low, int high)
    {
        int n = array.Length;

        for (int i = 0; i < nToRandomise; i++)
        {
            int randId1 = Random.Range(0, n);
            float randP = Random.Range(0f, 0.1f);
            int randId2 = (int)(1.0f * randId1 - randP * n);

            if (randId1 != randId2)
            {
                if ((randId1 > low) && (randId1 < high))
                {
                    if ((randId2 > low) && (randId2 < high))
                    {
                        int t = array[randId1];
                        array[randId1] = array[randId2];
                        array[randId2] = t;
                    }
                }
            }
        }
    }

    void CopyMask(int[] in1, int[] in2)
    {
        for (int i = 0; i < in1.Length; i++)
        {
            in1[i] = in2[i];
        }
    }

	// Based on https://begeeben.wordpress.com/2012/08/21/heap-sort-in-c/
    public static int[] HeapSort(float[] input1)
    {
        //Build-Max-Heap
        int heapSize = input1.Length;
        int[] iorig = new int[heapSize];
        float[] input = new float[heapSize];

        for (int i = 0; i < iorig.Length; i++)
        {
            iorig[i] = i;
            input[i] = input1[i];
        }

        for (int p = (heapSize - 1) / 2; p >= 0; p--)
        {
            MaxHeapify(input, iorig, heapSize, p);
        }

        for (int i = input.Length - 1; i > 0; i--)
        {
            //Swap
            float temp = input[i];
            input[i] = input[0];
            input[0] = temp;

            int itemp = iorig[i];
            iorig[i] = iorig[0];
            iorig[0] = itemp;

            heapSize--;
            MaxHeapify(input, iorig, heapSize, 0);
        }

        return iorig;
    }

    static void MaxHeapify(float[] input, int[] iorig, int heapSize, int index)
    {
        int left = (index + 1) * 2 - 1;
        int right = (index + 1) * 2;
        int largest = 0;

        if (left < heapSize && input[left] > input[index])
        {
            largest = left;
        }
        else
        {
            largest = index;
        }

        if (right < heapSize && input[right] > input[largest])
        {
            largest = right;
        }

        if (largest != index)
        {
            float temp = input[index];
            input[index] = input[largest];
            input[largest] = temp;

            int itemp = iorig[index];
            iorig[index] = iorig[largest];
            iorig[largest] = itemp;

            MaxHeapify(input, iorig, heapSize, largest);
        }
    }

    // File writer	

    public int fileWriteMode = -1;

    // 0 - build
    // 1 - destroy
    // 2 = build and destroy

    public string filePath;

    public int numBuildFrames = 10;
    public int numDestroyFrames = 10;

    void WriteCurrentBuildIntoFile(int i)
    {
#if UNITY_EDITOR
#if !UNITY_WEBPLAYER
        RefreshDirectories();

        Mesh msh = originalMeshRuntime;
        Mesh newMesh = new Mesh();

        newMesh.vertices = msh.vertices;
        newMesh.normals = msh.normals;
        newMesh.uv = msh.uv;
        newMesh.triangles = msh.triangles;
        newMesh.subMeshCount = msh.subMeshCount;

        for (int j = 0; j < msh.subMeshCount; j++)
        {
            newMesh.SetIndices(msh.GetIndices(j), msh.GetTopology(j), j);
        }

        newMesh.RecalculateBounds();

        if (newMesh == null)
        {
            Debug.Log("newMesh == null");
        }

        string dirName = "Assets/models/BuildDestroyAmimations/" + filePath + "/";
        UnityEditor.AssetDatabase.CreateAsset(newMesh, dirName + i.ToString() + "b.asset");
        UnityEditor.AssetDatabase.Refresh();
#endif
#endif
    }

    void WriteCurrentDestroyIntoFile(List<GameObject> meshesList, List<Mesh> meshListM, int iStep)
    {
#if UNITY_EDITOR
#if !UNITY_WEBPLAYER

        List<Vector3> masterVertices = new List<Vector3>();
        List<Vector3> masterNormals = new List<Vector3>();
        List<Vector2> masterUV = new List<Vector2>();
        List<int> masterTriangles = new List<int>();
        List<List<int>> masterIndices = new List<List<int>>();
        List<MeshTopology> masterTopology = new List<MeshTopology>();

        for (int k = 0; k < meshesList.Count; k++)
        {

            Mesh msh = meshListM[k];
            
            int nVertsNow = masterVertices.Count;

            Vector3[] vertices1 = msh.vertices;
            Vector3[] normals1 = msh.normals;
            Vector2[] uv1 = msh.uv;

            Transform tr = meshesList[k].transform;
            Vector3 scale = tr.localScale;

            Quaternion thisRotInv = Quaternion.Euler(-transform.rotation.eulerAngles);

            for (int i = 0; i < vertices1.Length; i++)
            {
                Vector3 vertNew = tr.TransformPoint(vertices1[i]);
                vertNew = new Vector3(vertNew.x / scale.x, vertNew.y / scale.y, vertNew.z / scale.z);
                vertNew = thisRotInv * vertNew;
                
                masterVertices.Add(vertNew);
                masterNormals.Add(normals1[i]);
                masterUV.Add(uv1[i]);
            }

            int[] triangles1 = msh.triangles;

            for (int i = 0; i < triangles1.Length; i++)
            {
                masterTriangles.Add(triangles1[i] + nVertsNow);
            }

            for (int i = 0; i < msh.subMeshCount; i++)
            {
                int[] inds1 = msh.GetIndices(i);
                masterTopology.Add(msh.GetTopology(i));

                if (nVertsNow == 0)
                {
                    masterIndices.Add(new List<int>());
                }

                for (int j = 0; j < inds1.Length; j++)
                {
                    masterIndices[i].Add(inds1[j] + nVertsNow);
                }
            }
        }

        Mesh masterMesh = new Mesh();
        masterMesh.Clear();

        masterMesh.vertices = masterVertices.ToArray();
        masterMesh.normals = masterNormals.ToArray();
        masterMesh.uv = masterUV.ToArray();
        masterMesh.triangles = masterTriangles.ToArray();

        masterMesh.subMeshCount = masterIndices.Count;

        for (int i = 0; i < masterIndices.Count; i++)
        {
            masterMesh.SetIndices(masterIndices[i].ToArray(), masterTopology[i], i);
        }

        masterMesh.RecalculateBounds();

        RefreshDirectories();
        string dirName = "Assets/models/BuildDestroyAmimations/" + filePath + "/";
        UnityEditor.AssetDatabase.CreateAsset(masterMesh, dirName + iStep.ToString() + "d.asset");
        UnityEditor.AssetDatabase.Refresh();
#endif
#endif
    }

    Mesh CopyMesh(Mesh oldMesh)
    {
        Mesh newMesh = new Mesh();

        newMesh.Clear();

        newMesh.vertices = oldMesh.vertices;
        newMesh.normals = oldMesh.normals;
        newMesh.uv = oldMesh.uv;
        newMesh.triangles = oldMesh.triangles;

        newMesh.subMeshCount = oldMesh.subMeshCount;

        for (int i = 0; i < oldMesh.subMeshCount; i++)
        {
            newMesh.SetIndices(oldMesh.GetIndices(i), oldMesh.GetTopology(i), i);
        }

        newMesh.RecalculateBounds();

        return newMesh;
    }

    void RefreshDirectories()
    {
#if UNITY_EDITOR
#if !UNITY_WEBPLAYER

        string pth = @Application.dataPath + "/models/BuildDestroyAmimations/" + filePath;

        if (!Directory.Exists(pth))
        {
            Directory.CreateDirectory(pth);
        }

        UnityEditor.AssetDatabase.Refresh();
#endif
#endif
    }
}
