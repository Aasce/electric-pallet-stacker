package com.firevr.ble;

import android.Manifest;
import android.annotation.SuppressLint;
import android.app.Activity;
import android.bluetooth.BluetoothAdapter;
import android.bluetooth.BluetoothDevice;
import android.bluetooth.BluetoothGatt;
import android.bluetooth.BluetoothGattCallback;
import android.bluetooth.BluetoothGattCharacteristic;
import android.bluetooth.BluetoothGattDescriptor;
import android.bluetooth.BluetoothGattService;
import android.bluetooth.BluetoothManager;
import android.bluetooth.BluetoothProfile;
import android.bluetooth.le.BluetoothLeScanner;
import android.bluetooth.le.ScanCallback;
import android.bluetooth.le.ScanFilter;
import android.bluetooth.le.ScanRecord;
import android.bluetooth.le.ScanResult;
import android.bluetooth.le.ScanSettings;
import android.content.Context;
import android.content.pm.PackageManager;
import android.os.Build;

import java.util.Collections;
import java.util.Locale;
import java.util.UUID;

/** Minimal native Android transport for the one-script Unity FIRE VR BLE test. */
public final class FireVrBleBridge {
    public interface Listener {
        void onInitialized(boolean success, String detail);
        void onScanStarted();
        void onDeviceFound(String name, String address);
        void onScanFailed(String reason);
        void onConnectionState(boolean connected, String detail);
        void onGattVerified(boolean servicePass, boolean txPass, boolean rxPass, String detail);
        void onSubscribed(boolean success, String detail);
        void onNotification(byte[] data);
        void onWrite(boolean success, String detail);
    }

    private static final String DEVICE_NAME = "VR-ESP32-FIRE";
    private static final UUID SERVICE_UUID = UUID.fromString("7a1e0001-8e88-4c19-9e6a-31b9d0a4f001");
    private static final UUID TX_UUID = UUID.fromString("7a1e0002-8e88-4c19-9e6a-31b9d0a4f001");
    private static final UUID RX_UUID = UUID.fromString("7a1e0003-8e88-4c19-9e6a-31b9d0a4f001");
    private static final UUID CLIENT_CONFIGURATION_UUID =
            UUID.fromString("00002902-0000-1000-8000-00805f9b34fb");

    private final Activity activity;
    private final Listener listener;

    private BluetoothAdapter adapter;
    private BluetoothLeScanner scanner;
    private BluetoothDevice foundDevice;
    private BluetoothGatt gatt;
    private BluetoothGattCharacteristic txCharacteristic;
    private BluetoothGattCharacteristic rxCharacteristic;
    private boolean scanning;
    private boolean gattVerified;

    public FireVrBleBridge(Activity activity, Listener listener) {
        if (activity == null)
            throw new IllegalArgumentException("Activity is required.");
        if (listener == null)
            throw new IllegalArgumentException("Listener is required.");

        this.activity = activity;
        this.listener = listener;
    }

    public void initialize() {
        try {
            PackageManager packageManager = activity.getPackageManager();
            if (!packageManager.hasSystemFeature(PackageManager.FEATURE_BLUETOOTH_LE)) {
                listener.onInitialized(false, "This Android device does not support BLE.");
                return;
            }

            BluetoothManager manager =
                    (BluetoothManager) activity.getSystemService(Context.BLUETOOTH_SERVICE);
            if (manager == null) {
                listener.onInitialized(false, "BluetoothManager is unavailable.");
                return;
            }

            adapter = manager.getAdapter();
            if (adapter == null) {
                listener.onInitialized(false, "Bluetooth adapter is unavailable.");
                return;
            }

            if (!hasRuntimePermissions()) {
                listener.onInitialized(false, "Required Android Bluetooth permission is not granted.");
                return;
            }

            if (!adapter.isEnabled()) {
                listener.onInitialized(false, "Bluetooth is disabled. Enable it and retry.");
                return;
            }

            listener.onInitialized(true, "Adapter: " + adapter.getName());
        } catch (SecurityException exception) {
            listener.onInitialized(false, "Bluetooth permission error: " + safeMessage(exception));
        } catch (Exception exception) {
            listener.onInitialized(false, "Initialization error: " + safeMessage(exception));
        }
    }

    @SuppressLint("MissingPermission")
    public void startScan() {
        if (adapter == null) {
            listener.onScanFailed("BLE is not initialized.");
            return;
        }

        if (!hasRuntimePermissions()) {
            listener.onScanFailed("Required Android Bluetooth permission is not granted.");
            return;
        }

        try {
            stopScanInternal();
            foundDevice = null;
            scanner = adapter.getBluetoothLeScanner();
            if (scanner == null) {
                listener.onScanFailed("BluetoothLeScanner is unavailable. Bluetooth may be disabled.");
                return;
            }

            ScanFilter filter = new ScanFilter.Builder()
                    .setDeviceName(DEVICE_NAME)
                    .build();
            ScanSettings settings = new ScanSettings.Builder()
                    .setScanMode(ScanSettings.SCAN_MODE_LOW_LATENCY)
                    .build();

            scanning = true;
            scanner.startScan(Collections.singletonList(filter), settings, scanCallback);
            listener.onScanStarted();
        } catch (SecurityException exception) {
            scanning = false;
            listener.onScanFailed("Bluetooth permission error: " + safeMessage(exception));
        } catch (Exception exception) {
            scanning = false;
            listener.onScanFailed("Unable to start scan: " + safeMessage(exception));
        }
    }

    @SuppressLint("MissingPermission")
    public void stopScan() {
        try {
            stopScanInternal();
        } catch (SecurityException exception) {
            listener.onScanFailed("Unable to stop scan: " + safeMessage(exception));
        }
    }

    @SuppressLint("MissingPermission")
    public void connect() {
        if (foundDevice == null) {
            listener.onConnectionState(false, "No matching scan result is available.");
            return;
        }

        if (!hasRuntimePermissions()) {
            listener.onConnectionState(false, "Required Android Bluetooth permission is not granted.");
            return;
        }

        try {
            stopScanInternal();
            closeGatt();
            gattVerified = false;
            txCharacteristic = null;
            rxCharacteristic = null;
            gatt = foundDevice.connectGatt(activity, false, gattCallback, BluetoothDevice.TRANSPORT_LE);
            if (gatt == null)
                listener.onConnectionState(false, "connectGatt returned null.");
        } catch (SecurityException exception) {
            listener.onConnectionState(false, "Bluetooth permission error: " + safeMessage(exception));
        } catch (Exception exception) {
            listener.onConnectionState(false, "Connection error: " + safeMessage(exception));
        }
    }

    @SuppressLint("MissingPermission")
    public void subscribeTx() {
        if (!gattVerified || gatt == null || txCharacteristic == null) {
            listener.onSubscribed(false, "GATT and TX must be verified before subscribing.");
            return;
        }

        try {
            if (!gatt.setCharacteristicNotification(txCharacteristic, true)) {
                listener.onSubscribed(false, "setCharacteristicNotification returned false.");
                return;
            }

            BluetoothGattDescriptor descriptor =
                    txCharacteristic.getDescriptor(CLIENT_CONFIGURATION_UUID);
            if (descriptor == null) {
                listener.onSubscribed(false, "TX Client Characteristic Configuration descriptor is missing.");
                return;
            }

            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                int status = gatt.writeDescriptor(
                        descriptor,
                        BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE);
                if (status != BluetoothGatt.GATT_SUCCESS)
                    listener.onSubscribed(false, "Descriptor write was rejected: status=" + status + ".");
            } else {
                descriptor.setValue(BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE);
                if (!gatt.writeDescriptor(descriptor))
                    listener.onSubscribed(false, "Descriptor write was rejected.");
            }
        } catch (SecurityException exception) {
            listener.onSubscribed(false, "Bluetooth permission error: " + safeMessage(exception));
        } catch (Exception exception) {
            listener.onSubscribed(false, "Subscription error: " + safeMessage(exception));
        }
    }

    @SuppressLint("MissingPermission")
    public void writeRx(byte[] data) {
        if (!gattVerified || gatt == null || rxCharacteristic == null) {
            listener.onWrite(false, "Connection and RX GATT verification are required.");
            return;
        }

        if (data == null || data.length == 0) {
            listener.onWrite(false, "Cannot write an empty payload.");
            return;
        }

        try {
            byte[] copy = data.clone();
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                int status = gatt.writeCharacteristic(
                        rxCharacteristic,
                        copy,
                        BluetoothGattCharacteristic.WRITE_TYPE_NO_RESPONSE);
                listener.onWrite(
                        status == BluetoothGatt.GATT_SUCCESS,
                        status == BluetoothGatt.GATT_SUCCESS
                                ? "WRITE WITHOUT RESPONSE queued."
                                : "Android rejected write: status=" + status + ".");
            } else {
                rxCharacteristic.setWriteType(BluetoothGattCharacteristic.WRITE_TYPE_NO_RESPONSE);
                rxCharacteristic.setValue(copy);
                boolean queued = gatt.writeCharacteristic(rxCharacteristic);
                listener.onWrite(
                        queued,
                        queued ? "WRITE WITHOUT RESPONSE queued." : "Android rejected write.");
            }
        } catch (SecurityException exception) {
            listener.onWrite(false, "Bluetooth permission error: " + safeMessage(exception));
        } catch (Exception exception) {
            listener.onWrite(false, "Write error: " + safeMessage(exception));
        }
    }

    @SuppressLint("MissingPermission")
    public void disconnect() {
        try {
            stopScanInternal();
            if (gatt == null) {
                clearGattState();
                listener.onConnectionState(false, "No active GATT connection.");
                return;
            }

            gatt.disconnect();
        } catch (SecurityException exception) {
            closeGatt();
            listener.onConnectionState(false, "Disconnect permission error: " + safeMessage(exception));
        } catch (Exception exception) {
            closeGatt();
            listener.onConnectionState(false, "Disconnect error: " + safeMessage(exception));
        }
    }

    public void dispose() {
        try {
            stopScanInternal();
        } catch (Exception ignored) {
            // Unity object is being destroyed; there is no receiver for a recoverable scan error.
        }
        closeGatt();
        foundDevice = null;
        adapter = null;
    }

    private final ScanCallback scanCallback = new ScanCallback() {
        @Override
        @SuppressLint("MissingPermission")
        public void onScanResult(int callbackType, ScanResult result) {
            handleScanResult(result);
        }

        @Override
        @SuppressLint("MissingPermission")
        public void onBatchScanResults(java.util.List<ScanResult> results) {
            if (results == null)
                return;
            for (ScanResult result : results) {
                if (handleScanResult(result))
                    break;
            }
        }

        @Override
        public void onScanFailed(int errorCode) {
            scanning = false;
            listener.onScanFailed("Android scan error code " + errorCode + ".");
        }
    };

    private final BluetoothGattCallback gattCallback = new BluetoothGattCallback() {
        @Override
        @SuppressLint("MissingPermission")
        public void onConnectionStateChange(BluetoothGatt callbackGatt, int status, int newState) {
            if (newState == BluetoothProfile.STATE_CONNECTED && status == BluetoothGatt.GATT_SUCCESS) {
                listener.onConnectionState(true, "GATT connected.");
                if (!callbackGatt.discoverServices()) {
                    listener.onGattVerified(false, false, false, "discoverServices returned false.");
                    callbackGatt.disconnect();
                }
                return;
            }

            if (newState == BluetoothProfile.STATE_DISCONNECTED) {
                String detail = status == BluetoothGatt.GATT_SUCCESS
                        ? "GATT disconnected."
                        : "GATT disconnected with status=" + status + ".";
                if (callbackGatt == gatt)
                    closeGatt();
                else
                    callbackGatt.close();
                listener.onConnectionState(false, detail);
                return;
            }

            if (status != BluetoothGatt.GATT_SUCCESS) {
                if (callbackGatt == gatt)
                    closeGatt();
                else
                    callbackGatt.close();
                listener.onConnectionState(false, "GATT connection failed: status=" + status + ".");
            }
        }

        @Override
        public void onServicesDiscovered(BluetoothGatt callbackGatt, int status) {
            if (status != BluetoothGatt.GATT_SUCCESS) {
                listener.onGattVerified(false, false, false,
                        "Service discovery failed: status=" + status + ".");
                return;
            }

            BluetoothGattService service = callbackGatt.getService(SERVICE_UUID);
            boolean servicePass = service != null;
            txCharacteristic = servicePass ? service.getCharacteristic(TX_UUID) : null;
            rxCharacteristic = servicePass ? service.getCharacteristic(RX_UUID) : null;

            boolean txExists = txCharacteristic != null;
            boolean rxExists = rxCharacteristic != null;
            int txProperties = txExists ? txCharacteristic.getProperties() : 0;
            int rxProperties = rxExists ? rxCharacteristic.getProperties() : 0;

            boolean txPass = txExists
                    && hasProperty(txProperties, BluetoothGattCharacteristic.PROPERTY_READ)
                    && hasProperty(txProperties, BluetoothGattCharacteristic.PROPERTY_NOTIFY);
            boolean rxPass = rxExists
                    && hasProperty(rxProperties, BluetoothGattCharacteristic.PROPERTY_WRITE)
                    && hasProperty(rxProperties, BluetoothGattCharacteristic.PROPERTY_WRITE_NO_RESPONSE);

            gattVerified = servicePass && txPass && rxPass;
            String detail = String.format(
                    Locale.US,
                    "Expected service/TX/RX UUIDs only. TX properties=0x%02X; RX properties=0x%02X.",
                    txProperties,
                    rxProperties);
            listener.onGattVerified(servicePass, txPass, rxPass, detail);
        }

        @Override
        public void onDescriptorWrite(BluetoothGatt callbackGatt,
                                      BluetoothGattDescriptor descriptor,
                                      int status) {
            if (!CLIENT_CONFIGURATION_UUID.equals(descriptor.getUuid()))
                return;

            listener.onSubscribed(
                    status == BluetoothGatt.GATT_SUCCESS,
                    status == BluetoothGatt.GATT_SUCCESS
                            ? "CCCD write complete."
                            : "CCCD write failed: status=" + status + ".");
        }

        @Override
        public void onCharacteristicChanged(BluetoothGatt callbackGatt,
                                            BluetoothGattCharacteristic characteristic,
                                            byte[] value) {
            if (TX_UUID.equals(characteristic.getUuid()))
                listener.onNotification(value == null ? new byte[0] : value.clone());
        }

        @Override
        @SuppressWarnings("deprecation")
        public void onCharacteristicChanged(BluetoothGatt callbackGatt,
                                            BluetoothGattCharacteristic characteristic) {
            if (!TX_UUID.equals(characteristic.getUuid()))
                return;

            byte[] value = characteristic.getValue();
            listener.onNotification(value == null ? new byte[0] : value.clone());
        }
    };

    @SuppressLint("MissingPermission")
    private boolean handleScanResult(ScanResult result) {
        if (!scanning || result == null || result.getDevice() == null)
            return false;

        BluetoothDevice device = result.getDevice();
        String name = device.getName();
        if (name == null) {
            ScanRecord record = result.getScanRecord();
            if (record != null)
                name = record.getDeviceName();
        }

        if (!DEVICE_NAME.equals(name))
            return false;

        foundDevice = device;
        stopScanInternal();
        listener.onDeviceFound(name, device.getAddress());
        return true;
    }

    @SuppressLint("MissingPermission")
    private void stopScanInternal() {
        if (scanning && scanner != null)
            scanner.stopScan(scanCallback);
        scanning = false;
    }

    private boolean hasRuntimePermissions() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            return activity.checkSelfPermission(Manifest.permission.BLUETOOTH_SCAN)
                    == PackageManager.PERMISSION_GRANTED
                    && activity.checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT)
                    == PackageManager.PERMISSION_GRANTED;
        }

        return Build.VERSION.SDK_INT < Build.VERSION_CODES.M
                || activity.checkSelfPermission(Manifest.permission.ACCESS_FINE_LOCATION)
                == PackageManager.PERMISSION_GRANTED;
    }

    private static boolean hasProperty(int properties, int property) {
        return (properties & property) != 0;
    }

    @SuppressLint("MissingPermission")
    private void closeGatt() {
        BluetoothGatt oldGatt = gatt;
        gatt = null;
        clearGattState();
        if (oldGatt != null)
            oldGatt.close();
    }

    private void clearGattState() {
        gattVerified = false;
        txCharacteristic = null;
        rxCharacteristic = null;
    }

    private static String safeMessage(Exception exception) {
        String message = exception.getMessage();
        return message == null || message.trim().isEmpty()
                ? exception.getClass().getSimpleName()
                : message;
    }
}
