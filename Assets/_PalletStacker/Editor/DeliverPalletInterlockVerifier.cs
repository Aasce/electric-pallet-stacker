using System;
using System.Reflection;
using ElectricPalletStackers.Ble;
using ElectricPalletStackers.Gameplay;
using ElectricPalletStackers.PalletStackers;
using UnityEditor;
using UnityEngine;

internal static class DeliverPalletInterlockVerifier
{
    private const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Electric Pallet Stacker/Verify Deliver Pallet Interlock")]
    private static void Verify()
    {
        GameObject root = new GameObject("Deliver Pallet Interlock Verification");
        root.SetActive(false);

        try
        {
            Rigidbody body = root.AddComponent<Rigidbody>();
            PalletStackerRigidbodyMotor motor = root.AddComponent<PalletStackerRigidbodyMotor>();
            SetField(motor, "_body", body);

            GameObject mastObject = new GameObject("Mast");
            mastObject.transform.SetParent(root.transform);
            PalletStackerMast mast = mastObject.AddComponent<PalletStackerMast>();
            Transform forks = new GameObject("Forks").transform;
            forks.SetParent(mastObject.transform);
            SetField(mast, "forks", forks);
            SetField(mast, "_vehicleMotor", motor);
            SetField(motor, "_mast", mast);

            TrainingExperienceController training =
                root.AddComponent<TrainingExperienceController>();
            PalletStackerControlDriver driver =
                root.AddComponent<PalletStackerControlDriver>();
            SetField(driver, "_interlockMotor", motor);
            SetField(driver, "_interlockMast", mast);
            SetField(driver, "_trainingExperience", training);

            MethodInfo applyInterlocks = typeof(PalletStackerControlDriver).GetMethod(
                "ApplyOperationalInterlocks",
                InstancePrivate);
            if (applyInterlocks == null)
                throw new MissingMethodException("ApplyOperationalInterlocks was not found.");

            training.RestoreStage(TrainingExperienceStage.DeliverPallet);
            forks.localPosition = new Vector3(0f, mast.MaxHeight, 0f);

            PalletStackerDriveCommand combinedAtMaximum = Apply(
                applyInterlocks,
                driver,
                CreateCommand(1f, PalletStackerLiftState.Up));
            Require(
                combinedAtMaximum.TravelNormalized > 0f,
                "DeliverPallet must allow travel at maximum fork height.");
            Require(
                combinedAtMaximum.Lift == PalletStackerLiftState.Neutral,
                "DeliverPallet must block lift while travel is requested.");
            Require(
                GetField<bool>(motor, "_forkHeightTravelInterlockBypassed"),
                "DeliverPallet must bypass only the motor's fork-height travel lock.");
            Require(
                !GetField<bool>(mast, "_movementLiftInterlockBypassed"),
                "DeliverPallet must keep the movement/lift interlock enabled.");

            PalletStackerDriveCommand liftOnlyAtMaximum = Apply(
                applyInterlocks,
                driver,
                CreateCommand(0f, PalletStackerLiftState.Up));
            Require(
                liftOnlyAtMaximum.Lift == PalletStackerLiftState.Up,
                "Lift-only input must remain available at maximum fork height.");

            SetField(motor, "_currentSpeed", 0.5f);
            PalletStackerDriveCommand liftWhileMoving = Apply(
                applyInterlocks,
                driver,
                CreateCommand(0f, PalletStackerLiftState.Down));
            Require(
                liftWhileMoving.Lift == PalletStackerLiftState.Neutral,
                "Actual vehicle movement must block lift in DeliverPallet.");
            SetField(motor, "_currentSpeed", 0f);

            forks.localPosition = new Vector3(0f, mast.MinHeight, 0f);
            PalletStackerDriveCommand combinedAtMinimum = Apply(
                applyInterlocks,
                driver,
                CreateCommand(1f, PalletStackerLiftState.Down));
            Require(
                combinedAtMinimum.TravelNormalized > 0f &&
                combinedAtMinimum.Lift == PalletStackerLiftState.Neutral,
                "Travel/lift mutual exclusion must also apply at minimum fork height.");

            training.RestoreStage(TrainingExperienceStage.PickupPallet);
            forks.localPosition = new Vector3(0f, mast.MaxHeight, 0f);
            PalletStackerDriveCommand nonFinalAtMaximum = Apply(
                applyInterlocks,
                driver,
                CreateCommand(1f, PalletStackerLiftState.Neutral));
            Require(
                Mathf.Approximately(nonFinalAtMaximum.TravelNormalized, 0f),
                "Non-final phases must still block travel above the height threshold.");

            Debug.Log(
                "[VERIFY] DeliverPallet interlock PASS: travel works at every fork height; " +
                "travel and lift remain mutually exclusive; non-final height lock is unchanged.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static PalletStackerDriveCommand CreateCommand(
        float travel,
        PalletStackerLiftState lift)
    {
        return new PalletStackerDriveCommand(
            1,
            travel,
            0f,
            50f,
            lift,
            false,
            false,
            false,
            false,
            false);
    }

    private static PalletStackerDriveCommand Apply(
        MethodInfo method,
        PalletStackerControlDriver driver,
        PalletStackerDriveCommand command)
    {
        return (PalletStackerDriveCommand)method.Invoke(driver, new object[] { command });
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void SetField(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(name, InstancePrivate);
        if (field == null) throw new MissingFieldException(target.GetType().Name, name);
        field.SetValue(target, value);
    }

    private static T GetField<T>(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, InstancePrivate);
        if (field == null) throw new MissingFieldException(target.GetType().Name, name);
        return (T)field.GetValue(target);
    }
}
