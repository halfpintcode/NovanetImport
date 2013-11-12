﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Security.Policy;
using NLog;

namespace NovaNetImport
{
    class Program
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        static void Main(string[] args)
        {
            Logger.Info("Starting Novanet Import Service");
            
            var basePath = AppDomain.CurrentDomain.BaseDirectory;

            //get sites and load into list of siteInfo 
            var sites = GetSites();

            //iterate sites
            foreach (var si in sites)
            {
                Console.WriteLine("Site: " + si.Name);
                
                //get file list not yet imported
                var newLastDate = DateTime.MinValue;
                List<FileInfo> fileList = GetFileList(si, ref newLastDate);

                //get the column schema for checks insulin recommendation worksheet
                var dbColList = new List<DBnnColumn>();
                var strConn = ConfigurationManager.ConnectionStrings["Halfpint"].ToString();
                using (var conn = new SqlConnection(strConn))
                {
                    var cmd = new SqlCommand("SELECT * FROM Novanet", conn);
                    conn.Open();

                    var rdr = cmd.ExecuteReader(CommandBehavior.SchemaOnly);
                    for (int i = 0; i < rdr.FieldCount; i++)
                    {
                        var col = new DBnnColumn()
                        {
                            Name = rdr.GetName(i),
                            DataType = rdr.GetDataTypeName(i)
                        };

                        dbColList.Add(col);
                        var fieldType = rdr.GetFieldType(i);
                        if (fieldType != null)
                        {
                            col.FieldType = fieldType.ToString();
                        }

                        
                    }
                }//using (var conn = new SqlConnection(strConn))

                foreach (var file in fileList)
                {
                    var streamRdr = file.OpenText();
                    string line;
                    string[] colNameList= {};
                    var rows = 0;
                    while ((line = streamRdr.ReadLine()) != null)
                    {
                        var columns = line.Split(',');
                        if (rows == 0)
                        {
                            colNameList = (string[]) columns.Clone();
                            rows++;
                            continue;
                        }

                        for (int i=0; i<columns.Length-1; i++)
                        {
                            var col = columns[i];
                            var colName = colNameList[i];
                            var dbCol = dbColList.Find(x => x.Name == colName);
                            if (dbCol != null)
                                Console.WriteLine("Col name: " + colName);
                        }

                        rows++;

                    }
                }
            }

            Console.Read();
        }

        private static List<FileInfo> GetFileList(SiteInfo si, ref DateTime newLastDate)
        {
            var list = new List<FileInfo>();

            //get the parent path for this site
            var parentPath = ConfigurationManager.AppSettings["NovaNetUploadPath"];
            parentPath = Path.Combine(parentPath, si.SiteId);

            if (Directory.Exists(parentPath))
            {
                //get the folders (named after the computer name) 
                var folders = Directory.EnumerateDirectories(parentPath);
                foreach (var folder in folders)
                {
                    Console.WriteLine("Folder: " + folder);
                    var di = new DirectoryInfo(folder);
                    foreach (var file in di.GetFiles())
                    {
                        Console.WriteLine("file name: " + file.FullName);
                        if (! file.Name.ToUpper().StartsWith("PR"))
                        {
                            //skip all files except files that start with pr
                            //maybe archive file
                            continue;
                        }

                        //extract the date from the file name
                        var datePart = file.Name.Substring(2, 6);
                        var sDate = "20" + datePart.Substring(0, 2) + "/" + datePart.Substring(2, 2) + "/" +
                                    datePart.Substring(4, 2);
                        var fileDate = DateTime.Parse(sDate);
                        
                        Console.WriteLine(fileDate);
                        if (si.LastFileDate.HasValue)
                        {
                            //if the last date is greater than the file date
                            if (si.LastFileDate.Value.CompareTo(fileDate) >= 0)
                                continue;
                        }
                        if (newLastDate.CompareTo(fileDate) < 0)
                            newLastDate = fileDate;
                        list.Add(file);
                    }
                }
            }
            //get the file list

            return list;
        }

        private static IEnumerable<SiteInfo> GetSites()
        {
            var sil = new List<SiteInfo>();

            String strConn = ConfigurationManager.ConnectionStrings["Halfpint"].ToString();

            using (var conn = new SqlConnection(strConn))
            {
                try
                {
                    var cmd = new SqlCommand("", conn) { CommandType = System.Data.CommandType.StoredProcedure, CommandText = "GetSitesActive" };

                    conn.Open();
                    var rdr = cmd.ExecuteReader();
                    while (rdr.Read())
                    {
                        var si = new SiteInfo();
                        var pos = rdr.GetOrdinal("ID");
                        si.Id = rdr.GetInt32(pos);

                        pos = rdr.GetOrdinal("Name");
                        si.Name = rdr.GetString(pos);

                        pos = rdr.GetOrdinal("SiteID");
                        si.SiteId = rdr.GetString(pos);

                        pos = rdr.GetOrdinal("LastNovanetFileDateImported");
                        si.LastFileDate = rdr.IsDBNull(pos) ? (DateTime?) null : rdr.GetDateTime(pos);

                        sil.Add(si);
                    }
                    rdr.Close();
                }
                catch (Exception ex)
                {
                    Logger.Error(ex);
                }
            }
            return sil;
        }
    }

    public class SiteInfo
    {
        public int Id { get; set; }
        public string SiteId { get; set; }
        public string Name { get; set; }
        public DateTime? LastFileDate { get; set; }
    }

    public class DBnnColumn
    {
        public string Name { get; set; }
        public string DataType { get; set; }
        public string FieldType { get; set; }
        public string Value { get; set; }
    }
}
