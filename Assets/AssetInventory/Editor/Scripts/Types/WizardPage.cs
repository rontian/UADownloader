using System;

namespace AssetInventory
{
    public interface IWizardPage
    {
        string Title { get; }
        string Description { get; }
        bool IsCompleted { get; set; }
        bool CanProceed { get; }
        void Draw();
        void OnEnter();
        void OnExit();
    }

    [Serializable]
    public abstract class WizardPage : IWizardPage
    {
        public abstract string Title { get; }
        public abstract string Description { get; }
        public virtual bool IsCompleted { get; set; }
        public virtual bool CanProceed => true;
        public abstract void Draw();
        public virtual void OnEnter() {}
        public virtual void OnExit()
        {
            IsCompleted = true;
        }

        public override string ToString()
        {
            return $"Wizard Page '{Title}'";
        }
    }
}
