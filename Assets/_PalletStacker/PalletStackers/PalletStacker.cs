using UnityEngine;

namespace ElectricPalletStackers.PalletStackers
{
    public class PalletStacker : MonoBehaviour
    {
        [SerializeField] private PalletStackerMast _mast;

        public void SetMastHeight(float height, bool isNormalized = true)
        {
            if (isNormalized) _mast.SetTargetHeightNormalized(height);
            else _mast.SetTargetHeight(height);
        }
    }

}
