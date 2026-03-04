using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SQLite;
using UnityEngine;

namespace AssetInventory
{
    public static class DBAdapter
    {
        public const string DB_NAME = "AssetInventory.db";

        public static SQLiteConnection DB
        {
            get
            {
                if (_db == null) InitDB();
                return _db;
            }
        }

        public static string DBError { get; private set; }
        private static SQLiteConnection _db;

        private static void InitDB()
        {
            try
            {
                DBError = null;

                _db = new SQLiteConnection(GetDBPath(), SQLiteOpenFlags.Create | SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex);
                _db.BusyTimeout = TimeSpan.FromSeconds(10);

                //_db.Trace = true;
                //_db.Tracer += s => Debug.Log(s);

                _db.ExecuteScalar<string>($"PRAGMA journal_mode={AI.Config.dbJournalMode};");
                _db.ExecuteScalar<long>("PRAGMA mmap_size = 2000000000"); // allow up to ~2 GB to be mmap’d
                _db.Execute("PRAGMA temp_store = MEMORY"); // temp tables in RAM
                _db.Execute("PRAGMA case_sensitive_like = false;");
                _db.Execute("PRAGMA synchronous = NORMAL"); // speed vs. crash-safety
                _db.Execute("PRAGMA cache_size = -20000"); // 20,000 1-page cache in RAM
                _db.Execute("PRAGMA page_size = 8192");

                _db.CreateTable<Asset>();
                _db.CreateTable<AssetFile>();
                _db.CreateTable<AssetMedia>();
                _db.CreateTable<AppProperty>();
                _db.CreateTable<CustomAction>();
                _db.CreateTable<CustomActionStep>();
                _db.CreateTable<MetadataDefinition>();
                _db.CreateTable<MetadataAssignment>();
                _db.CreateTable<SavedSearch>();
                _db.CreateTable<Tag>();
                _db.CreateTable<TagAssignment>();
                _db.CreateTable<RelativeLocation>();
                _db.CreateTable<SystemData>();
                _db.CreateTable<Workspace>();
                _db.CreateTable<WorkspaceSearch>();

                _db.CreateIndex("AssetFile", new[] {"Type", "PreviewState", "Path"});
                _db.CreateIndex("Asset", new[] {"Exclude", "AssetSource"});
            }
            catch (Exception e)
            {
                DBError = e.Message;

                Debug.LogError($"Error opening database '{GetDBPath()}': {DBError}");
            }
        }

        public static long GetDBSize()
        {
            return new FileInfo(GetDBPath()).Length;
        }

        public static bool ColumnExists(string tableName, string columnName)
        {
            List<SQLiteConnection.ColumnInfo> cols = DB.GetTableInfo(tableName);
            return cols.Any(c => c.Name == columnName);
        }

        public static long Optimize()
        {
            long original = new FileInfo(GetDBPath()).Length;

            DB.Execute("vacuum;");

            // Keep SQLite’s planner stats up to date so it picks the best index
            DB.Execute("analyze;");
            DB.Execute("PRAGMA optimize;");

            return original - new FileInfo(GetDBPath()).Length;
        }

        public static string GetDBPath()
        {
            return IOUtils.PathCombine(AI.GetStorageFolder(), DB_NAME);
        }

        public static bool IsDBOpen()
        {
            return _db != null;
        }

        public static void Close()
        {
            if (_db == null) return;
            _db.Close();
            _db = null;
        }

        public static bool DeleteDB()
        {
            if (IsDBOpen()) Close();
            try
            {
                File.Delete(GetDBPath());
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
