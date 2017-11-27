﻿/* =======================================================================
Copyright 2017 Technische Universitaet Darmstadt, Fachgebiet fuer Stroemungsdynamik (chair of fluid dynamics)

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
*/

using BoSSS.Foundation;
using BoSSS.Foundation.Grid;
using BoSSS.Foundation.Grid.Classic;
using BoSSS.Foundation.XDG;
using ilPSP;
using ilPSP.Tracing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BoSSS.Solution {

    /// <summary>
    /// During mesh/grid adaptation (refinement or coarsening) <see cref="Application{T}.MpiRedistributeAndMeshAdapt(int, double)"/>, this is used to
    /// - backup/serialize objects on the original grid
    /// - restore/serialize objects on the refined grid
    /// </summary>
    public class RemeshingData : GridUpdateData {


        /// <summary>
        /// ctor;
        /// </summary>
        /// <param name="oldGrid"></param>
        /// <param name="oldTracker"></param>
        internal RemeshingData(IGridData oldGrid, LevelSetTracker oldTracker) {
            if(oldTracker != null && !object.ReferenceEquals(oldTracker.GridDat, oldGrid))
                throw new ArgumentException();
            m_OldGrid = oldGrid;
            m_OldTracker = oldTracker;
            m_oldJ = m_OldGrid.CellPartitioning.LocalLength;
        }

        /// <summary>
        /// Stores the backup data of DG Fields after mesh adaptation.
        /// - dictionary key: string reference to identify DG field
        /// - 1st value index: local cell index, new grid.
        /// - 2nd value index: enumeration of cells; for refined and conserved cells, this contains only one element; for cells which are coarsened, this corresponds to all cells of the coarsening cluster.
        /// - 3rd value index: DG coordinate index within cell.
        /// </summary>
        Dictionary<string, double[][][]> m_newDGFieldData_GridAdapta;

        /// <summary>
        /// How the cells in the old mesh relate to cells in the new mesh.
        /// </summary>
        GridCorrelation m_Old2NewCorr;

        /// <summary>
        /// Apply the resorting (including mesh adaptation).
        /// </summary>
        public void Resort(GridCorrelation r, GridData NewGrid) {
            using(new FuncTrace()) {
                this.GridAdaptation = false;
                m_Old2NewCorr = r;
                m_OldGrid = null;
                m_OldTracker = null;
                m_NewGrid = NewGrid;
                m_newJ = m_NewGrid.iLogicalCells.NoOfLocalUpdatedCells;

                // fix the sequence in which we serialize/de-serialize fields
                string[] FieldNames = this.m_oldDGFieldData.Keys.ToArray();
                int NoFields = FieldNames.Length;

                // format data for serialization
                double[][] oldFieldsData = new double[m_oldJ][]; // 1st: cell; 2nd enum
                {

                    double[][][] oldFields = FieldNames.Select(rstring => this.m_oldDGFieldData[rstring]).ToArray(); // 1st: field; 2nd: cell; 3rd: DG coord

                    for(int j = 0; j < m_oldJ; j++) {
                        int Ntot = 0;
                        for(int iF = 0; iF < NoFields; iF++)
                            Ntot += oldFields[iF][j].Length;

                        // pack the DG coords for each field into one array of the form
                        // { Np_f1, DGcoords_f1, Np_f2, DGcoords_f2, .... };

                        double[] data_j = new double[Ntot + NoFields];
                        int cnt = 0;
                        for(int iF = 0; iF < NoFields; iF++) {
                            double[] data_iF_j = oldFields[iF][j];
                            int Nj = data_iF_j.Length;
                            data_j[cnt] = Nj;
                            cnt++;
                            for(int n = 0; n < Nj; n++) {
                                data_j[cnt] = data_iF_j[n];
                                cnt++;
                            }
                        }
                        oldFieldsData[j] = data_j;
                    }
                    oldFields = null;
                    m_oldDGFieldData = null;
                }

                // redistribute data
                double[][][] newFieldsData = new double[m_newJ][][];
                {
                    //Resorting.ApplyToVector(oldFieldsData, newFieldsData, m_NewGrid.CellPartitioning);
                    r.ApplyToVector(oldFieldsData, newFieldsData, m_NewGrid.CellPartitioning);
                    oldFieldsData = null;
                    Debug.Assert(newFieldsData.Length == m_newJ);
                }

                // re-format re-distributed data
                m_newDGFieldData_GridAdapta = new Dictionary<string, double[][][]>();
                {
                    double[][][][] newFields = new double[FieldNames.Length][][][]; // indices: [field, cell idx, enum over coarsening, DG mode]
                    for(int iF = 0; iF < NoFields; iF++) {
                        newFields[iF] = new double[m_newJ][][];
                    }
                    for(int j = 0; j < m_newJ; j++) {
                        double[][] data_j = newFieldsData[j];
                        int L = data_j.Length; // cell cluster size (equal 1 for refined or conserved cells)

                        for(int iF = 0; iF < NoFields; iF++)
                            newFields[iF][j] = new double[L][];

                        for(int l = 0; l < L; l++) {
                            double[] data_jl = data_j[l];

                            int cnt = 0;
                            for(int iF = 0; iF < NoFields; iF++) {
                                int Nj = (int)data_jl[cnt];
                                cnt++;
                                double[] data_iF_jl = new double[Nj];
                                for(int n = 0; n < Nj; n++) {
                                    data_iF_jl[n] = data_jl[cnt];
                                    cnt++;
                                }
                                newFields[iF][j][l] = data_iF_jl;
                            }
                            Debug.Assert(cnt == data_jl.Length);
                        }
                    }

                    for(int iF = 0; iF < NoFields; iF++) {
                        m_newDGFieldData_GridAdapta.Add(FieldNames[iF], newFields[iF]);
                    }
                }
            }

        }

        /// <summary>
        /// Backup data for some DG field before update of grid.
        /// </summary>
        /// <param name="f"></param>
        /// <param name="Reference">
        /// Unique string reference under which the re-distributed data can be accessed later, within <see cref="RestoreDGField(DGField, string)"/>.
        /// </param>
        override public void BackupField(DGField f, string Reference) {
            if(!object.ReferenceEquals(f.GridDat, m_OldGrid)) {
                throw new ArgumentException("DG field seems to be assigned to some other grid.");
            }
            double[][] oldFieldsData = new double[m_oldJ][];
            m_oldDGFieldData.Add(Reference, oldFieldsData);

            if(f is ConventionalDGField) {
                Basis oldBasis = f.Basis;
                int Nj = oldBasis.Length;


                for(int j = 0; j < m_oldJ; j++) {
                    double[] data_j = new double[Nj];
                    for(int n = 0; n < Nj; n++) {
                        data_j[n] = f.Coordinates[j, n];
                    }

                    oldFieldsData[j] = data_j;
                }
            } else if(f is XDGField) {
                XDGField xf = f as XDGField;
                XDGBasis xb = xf.Basis;
                int Np = xb.NonX_Basis.Length;
                if(!object.ReferenceEquals(m_OldTracker, xb.Tracker))
                    throw new ArgumentException("LevelSetTracker seems duplicate, or something.");
                LevelSetTracker lsTrk = m_OldTracker;

                for(int j = 0; j < m_oldJ; j++) {
                    int NoOfSpc = lsTrk.GetNoOfSpecies(j);
                    
                    int Nj = xb.GetLength(j);
                    Debug.Assert(Nj == NoOfSpc * Np);

                    double[] data_j = new double[Nj + NoOfSpc + 1];
                    data_j[0] = NoOfSpc;
                    int c = 1;
                    for(int iSpc = 0; iSpc < NoOfSpc; iSpc++) {
                        SpeciesId spc = lsTrk.GetSpeciesIdFromIndex(j, iSpc);
                        data_j[c] = spc.cntnt;
                        c++;
                        for(int n = 0; n < Np; n++) {
                            data_j[c] = f.Coordinates[j, n + Np*iSpc];
                            c++;
                        }
                    }
                    Debug.Assert(data_j.Length == c);

                    oldFieldsData[j] = data_j;
                }


            } else {
                throw new NotSupportedException();
            }
        }


        /// <summary>
        /// Loads the DG coordinates after grid-redistribution.
        /// </summary>
        /// <param name="f"></param>
        /// <param name="Reference">
        /// Unique string reference under which data has been stored before grid-redistribution.
        /// </param>
        public override void RestoreDGField(DGField f, string Reference) {
            using(new FuncTrace()) {
                int newJ = this.m_newJ;
                GridData NewGrid = (GridData)m_NewGrid;
                int pDeg = f.Basis.Degree; //  Refined_TestData.Basis.Degree;

                if(!object.ReferenceEquals(NewGrid, f.Basis.GridDat))
                    throw new ArgumentException("DG field must be assigned to new grid.");


                int[][] TargMappingIdx = m_Old2NewCorr.GetTargetMappingIndex(NewGrid.CellPartitioning);
                double[][][] ReDistDGCoords = m_newDGFieldData_GridAdapta[Reference];
                Debug.Assert(ReDistDGCoords.Length == newJ);

                if(f is ConventionalDGField) {
                    // +++++++++++++++
                    // normal DG field
                    // +++++++++++++++

                    int Np = f.Basis.Length;

                    double[] temp = new double[Np];
                    double[] acc = new double[Np];

                    for(int j = 0; j < newJ; j++) {
                        if(TargMappingIdx[j] == null) {
                            // unchanged cell
                            Debug.Assert(ReDistDGCoords[j].Length == 1);
                            f.Coordinates.SetRow(j, ReDistDGCoords[j][0]);
                        } else {
                            int L = ReDistDGCoords[j].Length;
                            for(int l = 0; l < L; l++) {
                                DoCellj(j, f, NewGrid, pDeg, TargMappingIdx[j], ReDistDGCoords[j], l, m_Old2NewCorr, 0, 0, Np, temp, acc);
                            }
                        }
                    }
                } else if(f is XDGField) {
                    // +++++++++++++++++++++++
                    // treatment of XDG fields
                    // +++++++++++++++++++++++

                    // (as always, more difficult)

                    XDGField xf = f as XDGField;
                    LevelSetTracker lsTrk = xf.Basis.Tracker;
                    int Np = xf.Basis.NonX_Basis.Length;
                    double[] temp = new double[Np];
                    double[] acc = new double[Np];

                    if(!object.ReferenceEquals(m_NewTracker, lsTrk))
                        throw new ArgumentException("LevelSetTracker seems duplicate, or something.");                    

                    for(int j = 0; j < newJ; j++) {
                        int NoOfSpc =  lsTrk.GetNoOfSpecies(j);

                        if(TargMappingIdx[j] == null) {
                            // unchanged cell
                            Debug.Assert(ReDistDGCoords[j].Length == 1);
                            double[] ReDistDGCoords_jl = ReDistDGCoords[j][0];
                            Debug.Assert(ReDistDGCoords_jl.Length == NoOfSpc*Np + NoOfSpc + 1);
                            Debug.Assert(ReDistDGCoords_jl[0] == NoOfSpc);

                            int c = 1;
                            for(int iSpc = 0; iSpc < NoOfSpc; iSpc++) {
#if DEBUG
                                SpeciesId rcvSpc;
                                rcvSpc.cntnt = (int) ReDistDGCoords_jl[c];
                                Debug.Assert(rcvSpc == lsTrk.GetSpeciesIdFromIndex(j, iSpc));
#endif
                                c++;

                                for(int n = 0; n < Np; n++) {
                                    f.Coordinates[j, n + Np*iSpc] = ReDistDGCoords_jl[c];
                                    c++;
                                }
                            }
                            Debug.Assert(c == ReDistDGCoords_jl.Length);

                        } else {
                            // coarsening or refinement

                            int L = ReDistDGCoords[j].Length;
                            for(int l = 0; l < L; l++) {
                                double[] ReDistDGCoords_jl = ReDistDGCoords[j][l];
                                int NoSpcOrg = (int) ReDistDGCoords_jl[0]; // no of species in original cells

                                int c = 1;
                                for(int iSpcRecv = 0; iSpcRecv < NoSpcOrg; iSpcRecv++) { // loop over species in original cell

                                    SpeciesId rcvSpc;
                                    rcvSpc.cntnt = (int)ReDistDGCoords_jl[c];
                                    c++;

                                    int iSpc = lsTrk.GetSpeciesIndex(rcvSpc, j); // species index in new cell 
                                    Debug.Assert(iSpcRecv == iSpc || L > 1);

                                    int N0rcv = c;
                                    c += Np;

                                    DoCellj(j, xf, NewGrid, pDeg, TargMappingIdx[j], ReDistDGCoords[j], l, m_Old2NewCorr, N0rcv, Np * iSpc, Np, temp, acc);
                                }
                                Debug.Assert(c == ReDistDGCoords_jl.Length);
                            }
                        }
                    }
                } else {
                    throw new NotImplementedException();
                }
            }
        }

        static private void DoCellj(int j, DGField f, GridData NewGrid, int pDeg, int[] TargMappingIdx_j, double[][] ReDistDGCoords_j, int l, GridCorrelation Old2NewCorr, int N0rcv, int N0acc, int Np, double[] ReDistDGCoords_jl, double[] Coords_j) {
            Debug.Assert(ReDistDGCoords_j.Length == TargMappingIdx_j.Length);

            int iKref = NewGrid.Cells.GetRefElementIndex(j);

            Debug.Assert(Coords_j.Length == Np);
            Debug.Assert(ReDistDGCoords_jl.Length == Np);

            for(int n = 0; n < Np; n++) {
                Coords_j[n] = f.Coordinates[j, N0acc + n];
            }
            
            int L = ReDistDGCoords_j.Length;


            if(TargMappingIdx_j.Length == 1) {
                // ++++++++++
                // refinement
                // ++++++++++
                Debug.Assert(l == 0);
                MultidimensionalArray Trafo = Old2NewCorr.GetSubdivBasisTransform(iKref, TargMappingIdx_j[0], pDeg);

                for(int n = 0; n < Np; n++) {
                    ReDistDGCoords_jl[n] = ReDistDGCoords_j[0][N0rcv + n];
                }
                Trafo.gemv(1.0, ReDistDGCoords_jl, 1.0, Coords_j, transpose: false);
            } else {
                // ++++++++++
                // coarsening
                // ++++++++++


                var Trafo = Old2NewCorr.GetSubdivBasisTransform(iKref, TargMappingIdx_j[l], pDeg);

                for(int n = 0; n < Np; n++) {
                    ReDistDGCoords_jl[n] = ReDistDGCoords_j[l][N0rcv + n];
                }

                Trafo.gemv(1.0, ReDistDGCoords_jl, 1.0, Coords_j, transpose: true);



            }

            for(int n = 0; n < Np; n++) {
                f.Coordinates[j, N0acc + n] = Coords_j[n];
            }

        }
    }
}