#if UNITY_2021_2_OR_NEWER && ASSET_INVENTORY_OLLAMA
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OllamaSharp;
using OllamaSharp.Models;
using UnityEngine;
#endif

namespace AssetInventory
{
    public static class Intelligence
    {
        public const string OLLAMA_WEBSITE = "https://www.ollama.com";
        public const string OLLAMA_LIBRARY = "https://ollama.com/search?c=vision";
        public const string OLLAMA_SERVICE_URL = "http://localhost:11434";

        internal static readonly string[] SuggestedOllamaModels = {"qwen2.5vl:7b (recommended)", "qwen2.5vl:3b (still good, faster, lower memory requirements)", "llava:7b (good alternative, comes down to personal preference)"};
        internal static readonly string ModelPrompt =
            "Create a caption.\n"
            + "The image is most likely a preview image of an assets used in the Unity 3d game engine.\n"
            + "Use at most 30 words.\n"
            + "Focus on the main theme.\n"
            + "Do not include phrases like 'is shown', 'is displayed', 'game engine', 'asset'.\n"
            + "Do not mention the background if it is uniform or plain or neutral.\n"
            + "Do not mention where this item could be used.\n"
            + "Do not mention Unity or that this is an asset.";

#if UNITY_2021_2_OR_NEWER && ASSET_INVENTORY_OLLAMA
        internal static IEnumerable<Model> OllamaModels
        {
            get
            {
                if (_models == null)
                {
                    _ = LoadOllamaModels();
                }
                return _models;
            }
        }
        private static IEnumerable<Model> _models;
        internal static bool OllamaModelDownloaded(string name) => OllamaModels != null && OllamaModels.Any(m => m.Name == name || m.Name.StartsWith(name + ":"));

        internal static bool LoadingModels;
        internal static bool DownloadingModel;

        internal static bool IsOllamaInstalled
        {
            get
            {
                if (_ollamaInstalled == null)
                {
                    try
                    {
                        _ = OllamaInstalled();
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Error checking Ollama installation. Restart Unity to solve: {e.Message}");
                        _ollamaInstalled = false;
                    }
                }
                return _ollamaInstalled ?? false;
            }
        }
        private static bool? _ollamaInstalled;

        internal static string OllamaVersion => IsOllamaInstalled ? _ollamaVersion : null;
        internal static CancellationTokenSource OllamaDownloadToken;
        private static string _ollamaVersion;

        internal static void RefreshOllama()
        {
            _ollamaInstalled = null;
            _models = null;
            _ollamaVersion = null;

            LoadingModels = false;
            DownloadingModel = false;

            _ = OllamaInstalled();
            _ = LoadOllamaModels();
        }

        private static async Task OllamaInstalled()
        {
            _ollamaInstalled = false; // set state so it doesn't keep checking

            try
            {
                OllamaApiClient ollama = new OllamaApiClient(new Uri(OLLAMA_SERVICE_URL));
                if (await ollama.IsRunningAsync())
                {
                    _ollamaInstalled = true;
                    _ollamaVersion = await ollama.GetVersionAsync();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error checking Ollama installation: {e.Message}");
            }
        }

        private static async Task LoadOllamaModels()
        {
            if (LoadingModels) return;
            LoadingModels = true;

            try
            {
                OllamaApiClient ollama = new OllamaApiClient(new Uri(OLLAMA_SERVICE_URL));
                _models = await ollama.ListLocalModelsAsync();
            }
            catch (Exception e)
            {
                _models = Enumerable.Empty<Model>(); 

                Debug.LogError($"Error loading Ollama models: {e.Message}");
            }

            LoadingModels = false;
        }

        internal static async Task PullOllamaModel(string name, Action<PullModelResponse> statusCallback)
        {
            if (string.IsNullOrEmpty(name)) return;

            if (DownloadingModel) return;
            DownloadingModel = true;

            try
            {
                OllamaApiClient ollama = new OllamaApiClient(new Uri(OLLAMA_SERVICE_URL));
                using (OllamaDownloadToken = new CancellationTokenSource())
                {
                    await foreach (PullModelResponse status in ollama.PullModelAsync(name, OllamaDownloadToken.Token))
                    {
                        statusCallback?.Invoke(status);
                    }
                }
                _models = null; // force reload of models
            }
            catch (Exception e)
            {
                Debug.LogError($"Error downloading Ollama model {name}: {e.Message}");
            }

            DownloadingModel = false;
        }

        internal static async Task DeleteOllamaModel(string name)
        {
            try
            {
                OllamaApiClient ollama = new OllamaApiClient(new Uri(OLLAMA_SERVICE_URL));
                await ollama.DeleteModelAsync(new DeleteModelRequest {Model = name});
                _models = null; // force reload of models
            }
            catch (Exception e)
            {
                Debug.LogError($"Error deleting Ollama model {name}: {e.Message}");
            }
        }
#else
        internal static bool IsOllamaInstalled => false;
#endif
    }
}
