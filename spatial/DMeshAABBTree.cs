﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace g3
{
    public class DMeshAABBTree3
    {
        DMesh3 mesh;

        public DMeshAABBTree3(DMesh3 m)
        {
            mesh = m;
        }




        public void Build()
        {
            build_by_one_rings();
        }




        public int FindNearestTriangle(Vector3d p)
        {
            double fNearestSqr = double.MaxValue;
            int tNearID = -1;
            find_nearest_tri(root_index, p, ref fNearestSqr, ref tNearID);
            return tNearID;
        }
        void find_nearest_tri(int iBox, Vector3d p, ref double fNearestSqr, ref int tID)
        {
            int idx = box_to_index[iBox];
            if ( idx < triangles_end ) {            // triange-list case, array is [N t1 t2 ... tN]
                int num_tris = index_list[idx];
                for (int i = 1; i <= num_tris; ++i) {
                    int ti = index_list[idx + i];
                    double fTriDistSqr = MeshQueries.TriDistanceSqr(mesh, ti, p);
                    if ( fTriDistSqr < fNearestSqr ) {
                        fNearestSqr = fTriDistSqr;
                        tID = ti;
                    }
                }

            } else {                                // internal node, either 1 or 2 child boxes
                int iChild1 = index_list[idx];
                if ( iChild1 < 0 ) {                 // 1 child, descend if nearer than cur min-dist
                    iChild1 = (-iChild1) - 1;
                    double fChild1DistSqr = box_distance_sqr(iChild1, p);
                    if ( fChild1DistSqr <= fNearestSqr )
                        find_nearest_tri(iChild1, p, ref fNearestSqr, ref tID);

                } else {                            // 2 children, descend closest first
                    iChild1 = iChild1 - 1;
                    int iChild2 = index_list[idx + 1] - 1;

                    double fChild1DistSqr = box_distance_sqr(iChild1, p);
                    double fChild2DistSqr = box_distance_sqr(iChild2, p);
                    if (fChild1DistSqr < fChild2DistSqr) {
                        if (fChild1DistSqr < fNearestSqr) {
                            find_nearest_tri(iChild1, p, ref fNearestSqr, ref tID);
                            if (fChild2DistSqr < fNearestSqr)
                                find_nearest_tri(iChild2, p, ref fNearestSqr, ref tID);
                        }
                    } else {
                        if (fChild2DistSqr < fNearestSqr) {
                            find_nearest_tri(iChild2, p, ref fNearestSqr, ref tID);
                            if (fChild1DistSqr < fNearestSqr)
                                find_nearest_tri(iChild1, p, ref fNearestSqr, ref tID);
                        }
                    }

                }
            }
        }






        // DoTraversal function will walk through tree and call NextBoxF for each
        //  internal box node, and NextTriangleF for each triangle. 
        //  You can prune branches by returning false from NextBoxF
        public class TreeTraversal
        {
            // return false to terminate this branch
            public Func<AxisAlignedBox3f, bool> NextBoxF = (x) => { return true; };

            public Action<int> NextTriangleF = (tID) => { };
        }


        // walk over tree, calling functions in TreeTraversal object for internal nodes and triangles
        public void DoTraversal(TreeTraversal traversal)
        {
            tree_traversal(root_index, traversal);
        }

        // traversal implementation
        private void tree_traversal(int iBox, TreeTraversal traversal)
        {
            int idx = box_to_index[iBox];

            if ( idx < triangles_end ) {
                // triange-list case, array is [N t1 t2 ... tN]
                int n = index_list[idx];
                for ( int i = 1; i <= n; ++i ) {
                    int ti = index_list[idx + i];
                    traversal.NextTriangleF(ti);
                }
            } else {
                int i0 = index_list[idx];
                if ( i0 < 0 ) {
                    // negative index means we only have one 'child' box to descend into
                    i0 = (-i0) - 1;
                    if ( traversal.NextBoxF(get_box(i0)) )
                        tree_traversal(i0, traversal);
                } else {
                    // positive index, two sequential child box indices to descend into
                    i0 = i0 - 1;
                    if ( traversal.NextBoxF(get_box(i0)) )
                        tree_traversal(i0, traversal);
                    int i1 = index_list[idx + 1] - 1;
                    if ( traversal.NextBoxF(get_box(i1)) )
                        tree_traversal(i0, traversal);
                }
            }
        }




        //
        // Internals - data structures, construction, etc
        //




        // storage for box nodes. 
        //   - box_to_index is a pointer into index_list
        //   - box_centers and box_extents are the centers/extents of the bounding boxes
        DVector<int> box_to_index;
        DVector<Vector3f> box_centers;
        DVector<Vector3f> box_extents;

        // list of indices for a given box. There is *no* marker/sentinel between
        // boxes, you have to get the starting index from box_to_index[]
        //
        // There are three kinds of records:
        //   - if i < triangles_end, then the list is a number of triangles,
        //       stored as [N t1 t2 t3 ... tN]
        //   - if i > triangles_end and index_list[i] < 0, this is a single-child
        //       internal box, with index (-index_list[i])-1     (shift-by-one in case actual value is 0!)
        //   - if i > triangles_end and index_list[i] > 0, this is a two-child
        //       internal box, with indices index_list[i]-1 and index_list[i+1]-1
        DVector<int> index_list;

        // index_list[i] for i < triangles_end is a triangle-index list, otherwise box-index pair/single
        int triangles_end = -1;

        // box_to_index[root_index] is the root node of the tree
        int root_index = -1;






        // strategy here is:
        //  1) partition triangles by vertex one-rings into leaf boxes
        //      1a) first pass where we skip one-rings that have < 3 free tris
        //      1b) second pass where we handle any missed tris
        //  2) sequentially combine N leaf boxes into (N/2 + N%2) layer 2 boxes
        //  3) repeat until layer K has only 1 box, which is root of tree
        void build_by_one_rings()
        {
            box_to_index = new DVector<int>();
            box_centers = new DVector<Vector3f>();
            box_extents = new DVector<Vector3f>();
            int iBoxCur = 0;

            index_list = new DVector<int>();
            int iIndicesCur = 0;

            // replace w/ BitArray ?
            byte[] used_triangles = new byte[mesh.MaxTriangleID];
            Array.Clear(used_triangles, 0, used_triangles.Length);

            // temporary buffer
            int nMaxEdgeCount = mesh.GetMaxVtxEdgeCount();
            int[] temp_tris = new int[2*nMaxEdgeCount];

            // first pass: cluster by one-ring, but if # of free tris
            //  in a ring is small (< 3), push onto spill list to try again,
            //  because those tris might be picked up by a bigger cluster
            DVector<int> spill = new DVector<int>();
            foreach ( int vid in mesh.VertexIndices() ) {
                int tCount = add_one_ring_box(vid, used_triangles, temp_tris,
                    ref iBoxCur, ref iIndicesCur, spill, 3);
                if (tCount < 3)
                    spill.Add(vid);
            }

            // second pass: check any spill vertices. Most are probably gone 
            // now, but a few stray triangles might still exist
            int N = spill.Length;
            for ( int si = 0; si < N; ++si ) {
                int vid = spill[si];
                add_one_ring_box(vid, used_triangles, temp_tris,
                    ref iBoxCur, ref iIndicesCur, null, 0);
            }


            // [RMS] test code to make sure each triangle is in exactly one list
            //foreach ( int tid in mesh.TriangleIndices() ) {
            //    int n = used_triangles[tid];
            //    if (n != 1)
            //        Util.gBreakToDebugger();
            //}

            // keep track of where triangle lists end
            triangles_end = iIndicesCur;

            // ok, now repeatedly cluster current layer of N boxes into N/2 + N%2 boxes,
            // until we hit a 1-box layer, which is root of the tree
            int nPrevEnd = iBoxCur;
            int nLayerSize = cluster_boxes(0, iBoxCur, ref iBoxCur, ref iIndicesCur);
            int iStart = nPrevEnd;
            int iCount = iBoxCur - nPrevEnd;
            while ( nLayerSize > 1 ) {
                nPrevEnd = iBoxCur;
                nLayerSize = cluster_boxes(iStart, iCount, ref iBoxCur, ref iIndicesCur);
                iStart = nPrevEnd;
                iCount = iBoxCur - nPrevEnd;
            }

            root_index = iBoxCur - 1;
        }



        // Appends a box that contains free triangles in one-ring of vertex vid.
        // If tri count is < spill threshold, push onto spill list instead.
        // Returns # of free tris found.
        int add_one_ring_box(int vid, byte[] used_triangles, int[] temp_tris, 
            ref int iBoxCur, ref int iIndicesCur,
            DVector<int> spill, int nSpillThresh )
        {
            // collect free triangles
            int num_free = 0;
            foreach ( int tid in mesh.VtxTrianglesItr(vid) ) {
                if ( used_triangles[tid] == 0 ) 
                    temp_tris[num_free++] = tid;
            }

            // none free, get out
            if (num_free == 0)
                return 0;

            // if we only had a couple free triangles, wait and see if
            // they get picked up by another vert
            if (num_free < nSpillThresh) {
                spill.Add(vid);
                return num_free;
            }

            // append new box
            AxisAlignedBox3f box = AxisAlignedBox3f.Empty;
            int iBox = iBoxCur++;
            box_to_index.insert(iIndicesCur, iBox);

            index_list.insert(num_free, iIndicesCur++);
            for (int i = 0; i < num_free; ++i) {
                index_list.insert(temp_tris[i], iIndicesCur++);
                used_triangles[temp_tris[i]]++;     // incrementing for sanity check below, just need to set to 1
                box.Contain(mesh.GetTriBounds(temp_tris[i]));
            }

            box_centers.insert(box.Center, iBox);
            box_extents.insert(box.Extents, iBox);
            return num_free;
        }





        // Turn a span of N boxes into N/2 boxes, by pairing boxes
        // Except, of course, if N is odd, then we get N/2+1, where the +1
        // box has a single child box (ie just a copy).
        // [TODO] instead merge that extra box into on of parents? Reduces tree depth by 1
        int cluster_boxes(int iStart, int iCount, ref int iBoxCur, ref int iIndicesCur)
        {
            int[] indices = new int[iCount];
            for (int i = 0; i < iCount; ++i)
                indices[i] = iStart + i;

            int nDim = 0;
            Array.Sort(indices, (a, b) => {
                float axis_min_a = box_centers[a][nDim] - box_extents[a][nDim];
                float axis_min_b = box_centers[b][nDim] - box_extents[b][nDim];
                return (axis_min_a == axis_min_b) ? 0 :
                            (axis_min_a < axis_min_b) ? -1 : 1;
            });

            int nPairs = iCount / 2;
            int nLeft = iCount - 2 * nPairs;

            // this is dumb! but lets us test the rest...
            for ( int pi = 0; pi < nPairs; pi++ ) {
                int i0 = indices[2*pi];
                int i1 = indices[2*pi + 1];

                Vector3f center, extent;
                get_combined_box(i0, i1, out center, out extent);

                // append new box
                int iBox = iBoxCur++;
                box_to_index.insert(iIndicesCur, iBox);

                index_list.insert(i0+1, iIndicesCur++);
                index_list.insert(i1+1, iIndicesCur++);

                box_centers.insert(center, iBox);
                box_extents.insert(extent, iBox);
            }

            // [todo] could we merge with last other box? need a way to tell
            //   that there are 3 children though...could use negative index for that?
            if ( nLeft > 0 ) {
                if (nLeft > 1)
                    Util.gBreakToDebugger();

                int iLeft = indices[2*nPairs];

                // duplicate box at this level... ?
                int iBox = iBoxCur++;
                box_to_index.insert(iIndicesCur, iBox);

                // negative index means only one child
                index_list.insert(-(iLeft+1), iIndicesCur++);
                
                box_centers.insert(box_centers[iLeft], iBox);
                box_extents.insert(box_extents[iLeft], iBox);
            }

            return nPairs + nLeft;
        }






        // construct box that contains two boxes
        void get_combined_box(int b0, int b1, out Vector3f center, out Vector3f extent)
        {
            Vector3f c0 = box_centers[b0];
            Vector3f e0 = box_extents[b0];
            Vector3f c1 = box_centers[b1];
            Vector3f e1 = box_extents[b1];

            float minx = Math.Min(c0.x - e0.x, c1.x - e1.x);
            float maxx = Math.Max(c0.x + e0.x, c1.x + e1.x);
            float miny = Math.Min(c0.y - e0.y, c1.y - e1.y);
            float maxy = Math.Max(c0.y + e0.y, c1.y + e1.y);
            float minz = Math.Min(c0.z - e0.z, c1.z - e1.z);
            float maxz = Math.Max(c0.z + e0.z, c1.z + e1.z);

            center = new Vector3f(0.5f * (minx + maxx), 0.5f * (miny + maxy), 0.5f * (minz + maxz));
            extent = new Vector3f(0.5f * (maxx - minx), 0.5f * (maxy - miny), 0.5f * (maxz - minz));
        }


        AxisAlignedBox3f get_box(int iBox)
        {
            Vector3f c = box_centers[iBox];
            Vector3f e = box_extents[iBox];
            e += 10.0f*MathUtil.Epsilonf;      // because of float/double casts, box may drift to the point
                                               // where mesh vertex will be slightly outside box
            return new AxisAlignedBox3f(c - e, c + e);
        }



        double box_distance_sqr(int iBox, Vector3d p)
        {
            Vector3d c = box_centers[iBox];
            Vector3d e = box_extents[iBox];
            AxisAlignedBox3d box = new AxisAlignedBox3d(c - e, c + e);
            return box.DistanceSquared(p);
        }

        




        // 1) make sure we can reach every tri in mesh through tree (also demo of how to traverse tree...)
        // 2) make sure that triangles are contained in parent boxes
        public void TestCoverage()
        {
            int[] tri_counts = new int[mesh.MaxTriangleID];
            Array.Clear(tri_counts, 0, tri_counts.Length);
            int[] parent_indices = new int[box_to_index.Length];
            Array.Clear(parent_indices, 0, parent_indices.Length);

            test_coverage(tri_counts, parent_indices, root_index);

            foreach (int ti in mesh.TriangleIndices())
                if (tri_counts[ti] != 1)
                    Util.gBreakToDebugger();
        }

        // accumulate triangle counts and track each box-parent index. 
        // also checks that triangles are contained in boxes
        private void test_coverage(int[] tri_counts, int[] parent_indices, int iBox)
        {
            int idx = box_to_index[iBox];

            debug_check_child_tris_in_box(iBox);

            if ( idx < triangles_end ) {
                // triange-list case, array is [N t1 t2 ... tN]
                int n = index_list[idx];
                AxisAlignedBox3f box = get_box(iBox);
                for ( int i = 1; i <= n; ++i ) {
                    int ti = index_list[idx + i];
                    tri_counts[ti]++;

                    Index3i tv = mesh.GetTriangle(ti);
                    for ( int j = 0; j < 3; ++j ) {
                        Vector3f v = (Vector3f)mesh.GetVertex(tv[j]);
                        if (!box.Contains(v))
                            Util.gBreakToDebugger();
                    }
                }

            } else {
                int i0 = index_list[idx];
                if ( i0 < 0 ) {
                    // negative index means we only have one 'child' box to descend into
                    i0 = (-i0) - 1;
                    parent_indices[i0] = iBox;
                    test_coverage(tri_counts, parent_indices, i0);
                } else {
                    // positive index, two sequential child box indices to descend into
                    i0 = i0 - 1;
                    parent_indices[i0] = iBox;
                    test_coverage(tri_counts, parent_indices, i0);
                    int i1 = index_list[idx + 1];
                    i1 = i1 - 1;
                    parent_indices[i1] = iBox;
                    test_coverage(tri_counts, parent_indices, i1);
                }
            }
        }
        // do full tree traversal below iBox and make sure that all triangles are further
        // than box-distance-sqr
        void debug_check_child_tri_distances(int iBox, Vector3d p)
        {
            double fBoxDistSqr = box_distance_sqr(iBox, p);

            TreeTraversal t = new TreeTraversal() {
                NextTriangleF = (tID) => {
                    double fTriDistSqr = MeshQueries.TriDistanceSqr(mesh, tID, p);
                    if (fTriDistSqr < fBoxDistSqr)
                        if ( Math.Abs(fTriDistSqr - fBoxDistSqr) > MathUtil.ZeroTolerance*100 )
                            Util.gBreakToDebugger();
                }
            };
            tree_traversal(iBox, t);
        }

        // do full tree traversal below iBox to make sure that all child triangles are contained
        void debug_check_child_tris_in_box(int iBox)
        {
            AxisAlignedBox3f box = get_box(iBox);
            TreeTraversal t = new TreeTraversal() {
                NextTriangleF = (tID) => {
                    Index3i tv = mesh.GetTriangle(tID);
                    for (int j = 0; j < 3; ++j) {
                        Vector3f v = (Vector3f)mesh.GetVertex(tv[j]);
                        if (box.Contains(v) == false)
                            Util.gBreakToDebugger();
                    }
                }
            };
            tree_traversal(iBox, t);
        }



    }
}