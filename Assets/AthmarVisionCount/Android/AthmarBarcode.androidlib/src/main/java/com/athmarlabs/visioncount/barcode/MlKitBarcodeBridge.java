package com.athmarlabs.visioncount.barcode;

import android.graphics.Bitmap;
import android.graphics.BitmapFactory;

import com.google.mlkit.vision.barcode.BarcodeScanner;
import com.google.mlkit.vision.barcode.BarcodeScannerOptions;
import com.google.mlkit.vision.barcode.BarcodeScanning;
import com.google.mlkit.vision.barcode.common.Barcode;
import com.google.mlkit.vision.common.InputImage;

import java.util.LinkedHashSet;
import java.util.List;
import java.util.Set;

public final class MlKitBarcodeBridge {
    public interface Callback {
        void onSuccess(String value);
        void onNoResult();
        void onError(String message);
    }

    private static BarcodeScanner scanner;

    private MlKitBarcodeBridge() { }

    public static boolean isAvailable() {
        return true;
    }

    public static void scanJpeg(byte[] jpegBytes, Callback callback) {
        if (callback == null) return;
        if (jpegBytes == null || jpegBytes.length == 0) {
            callback.onError("Barcode image is empty.");
            return;
        }

        final Bitmap bitmap = BitmapFactory.decodeByteArray(jpegBytes, 0, jpegBytes.length);
        if (bitmap == null) {
            callback.onError("Barcode image could not be decoded.");
            return;
        }

        final InputImage image = InputImage.fromBitmap(bitmap, 0);
        getScanner().process(image)
            .addOnSuccessListener(barcodes -> {
                try {
                    String value = exactlyOneValue(barcodes);
                    if (value == null || value.isEmpty()) callback.onNoResult();
                    else callback.onSuccess(value);
                } finally {
                    bitmap.recycle();
                }
            })
            .addOnFailureListener(exception -> {
                bitmap.recycle();
                String message = exception == null ? "ML Kit barcode scan failed." : exception.getMessage();
                callback.onError(message == null ? "ML Kit barcode scan failed." : message);
            });
    }

    private static synchronized BarcodeScanner getScanner() {
        if (scanner != null) return scanner;
        BarcodeScannerOptions options = new BarcodeScannerOptions.Builder()
            .setBarcodeFormats(
                Barcode.FORMAT_EAN_13,
                Barcode.FORMAT_EAN_8,
                Barcode.FORMAT_UPC_A,
                Barcode.FORMAT_UPC_E,
                Barcode.FORMAT_CODE_128,
                Barcode.FORMAT_CODE_39,
                Barcode.FORMAT_ITF,
                Barcode.FORMAT_CODABAR,
                Barcode.FORMAT_DATA_MATRIX,
                Barcode.FORMAT_QR_CODE)
            .build();
        scanner = BarcodeScanning.getClient(options);
        return scanner;
    }

    private static String exactlyOneValue(List<Barcode> barcodes) {
        Set<String> values = new LinkedHashSet<>();
        if (barcodes != null) {
            for (Barcode barcode : barcodes) {
                if (barcode == null) continue;
                String value = barcode.getRawValue();
                if (value != null && !value.trim().isEmpty()) values.add(value.trim());
                if (values.size() > 1) return null;
            }
        }
        return values.size() == 1 ? values.iterator().next() : null;
    }
}
