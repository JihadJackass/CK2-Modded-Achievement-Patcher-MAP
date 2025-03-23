import pymem
import pymem.process
import psutil
import time
import threading
import struct
import re
import ctypes
import tkinter as tk
from tkinter import messagebox

running = False
launcher_detected = False

# Memory patches extracted from the Cheat Engine script
aob_patterns = {
    #"console": b"\x48\x8B\xD9\x80\xB8\x00\x05\x00\x00\x00\x75",
    #"enable_console": b"\x8B\x1D....\x4C\x39\xAB\xF0\x04\x00\x00",
    #"uncorrupt_save": b"\xE8....\xC6\x40\x61\x00\x0F\xB6\x45\xA7\x88",
    "ironman": b"\x0F\xB6\x83\xF9\x02\x00\x00\x48",
    "ruler_designer": b"\xC6\x40\x62\x01\x48\x8B\x8B\x10\x6C\x00\x00",
    #"savegame_check": b"\x88\x46\x61\x48\x8B\x03",
    #"checksum_check": b"\x88\x4F\x63\x48\x8B\x46\x30",
}

memory_patches = {
    #"console": b"\x90\x90\x90\x90\x90\x39\xC0",
    #"enable_console": b"\x90\x90\x90\x90\x90\x39\xC0",
    #"uncorrupt_save": b"\xC6\x40\x61\x01",
    "ironman": b"\xC6\x83\xf9\x02\x00\x00\x01",
    "ruler_designer": b"\xC6\x40\x62\x00\xC6\x40\x63\x01\xC6\x40\x65\x01",
    #"savegame_check": b"\x90\x90\x90\x90\x90",
    #"checksum_check": b"\xC6\x07\x01",
}

def find_pattern(pm, module_base, module_size, pattern_bytes):
    """ Scans memory for the given pattern and returns the address while respecting wildcards """
    try:
        memory_dump = pm.read_bytes(module_base, module_size)
        regex_pattern = re.escape(pattern_bytes).replace(b"\\.\\.\\.\\.", b".{4}")
        match = re.search(regex_pattern, memory_dump, re.DOTALL)
        if match:
            log_message(f"Found {pattern_bytes} at {hex(module_base + match.start())}")
            return module_base + match.start()
        log_message(f"ERROR: Pattern not found for {pattern_bytes}")
        return None
    except Exception as e:
        log_message(f"ERROR: Memory scan failed - {e}")
        return None

def patch_memory():
    try:
        pm = pymem.Pymem("CK2game.exe")
        module = pymem.process.module_from_name(pm.process_handle, "CK2game.exe")
        module_base = module.lpBaseOfDll
        module_size = module.SizeOfImage

        if not module_base:
            log_message("ERROR: Could not find CK2game.exe module base address.")
            return

        log_message(f"CK2game.exe Base Address: {hex(module_base)}, Size: {hex(module_size)}")

        # Define VirtualProtectEx for memory protection changes
        kernel32 = ctypes.windll.kernel32
        kernel32.VirtualProtectEx.argtypes = [
            ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t, ctypes.c_uint32, ctypes.POINTER(ctypes.c_uint32)
        ]
        kernel32.VirtualProtectEx.restype = ctypes.c_bool

        for key, pattern_bytes in aob_patterns.items():
            address = find_pattern(pm, module_base, module_size, pattern_bytes)

            if address:
                patch_bytes = memory_patches[key]
                log_message(f"Applying patch to {key} at {hex(address)}: {patch_bytes}")

                try:
                    original_bytes = pm.read_bytes(address, len(patch_bytes))
                    log_message(f"Original bytes at {hex(address)}: {original_bytes}")

                    # Temporarily change memory protection
                    old_protect = ctypes.c_uint32()
                    PAGE_EXECUTE_READWRITE = 0x40
                    address_ptr = ctypes.c_void_p(address)

                    success = kernel32.VirtualProtectEx(
                        ctypes.c_void_p(pm.process_handle),
                        address_ptr,
                        ctypes.c_size_t(len(patch_bytes)),
                        PAGE_EXECUTE_READWRITE,
                        ctypes.byref(old_protect)
                    )
                    if not success:
                        log_message(f"ERROR: VirtualProtectEx failed for {key} at {hex(address)}")
                        continue

                    log_message(f"Memory protection changed for {key} at {hex(address)}")

                    # Apply patch
                    pm.write_bytes(address, patch_bytes, len(patch_bytes))

                    # Verify patch
                    new_bytes = pm.read_bytes(address, len(patch_bytes))
                    if new_bytes == patch_bytes:
                        log_message(f"Patched {key} at {hex(address)}")
                    else:
                        log_message(f"WARNING: Patch {key} may have been reverted by CK2! Expected: {patch_bytes}, Found: {new_bytes}")

                    # Restore original memory protection
                    kernel32.VirtualProtectEx(
                        ctypes.c_void_p(pm.process_handle),
                        address_ptr,
                        ctypes.c_size_t(len(patch_bytes)),
                        old_protect.value,
                        ctypes.byref(old_protect)
                    )

                except Exception as e:
                    log_message(f"ERROR: Failed to write patch for {key} at {hex(address)} - {e}")

        log_message("Patching complete!")
        monitor_button.config(text="CK2game.exe Patched")

    except Exception as e:
        log_message(f"FATAL ERROR: {e}")
        messagebox.showerror("Error", f"Failed to apply memory patch: {e}")

def get_ck2_process():
    """ Detects CK2game.exe while it's still in the launcher phase """
    for proc in psutil.process_iter(attrs=['pid', 'name']):
        if proc.info['name'].lower() == 'ck2game.exe':
            return proc.info['pid']
    return None

def monitor_ck2():
    global running, launcher_detected
    monitor_button.config(text="Waiting for CK2 launcher...", state=tk.DISABLED)
    
    while running:
        pid = get_ck2_process()
        if pid:
            if not launcher_detected:
                log_message("Detected CK2 launcher. Applying patch now...")
                patch_memory()
                log_message("Patches applied. You can now start the game.")
                running = False  # Stop monitoring after patching
                return

        time.sleep(1)
    
    monitor_button.config(text="Start Monitoring", state=tk.NORMAL)

def start_monitoring():
    global running, launcher_detected
    if not running:
        running = True
        launcher_detected = False
        threading.Thread(target=monitor_ck2, daemon=True).start()
        log_message("Waiting for CK2 launcher to start...")
        monitor_button.config(text="Waiting for CK2 launcher...", state=tk.DISABLED)

def stop_monitoring():
    global running
    running = False
    monitor_button.config(text="Start Monitoring", state=tk.NORMAL)
    log_message("Stopped monitoring for CK2 launcher.")

def log_message(message):
    log_text.insert(tk.END, message + "\n")
    log_text.see(tk.END)

# GUI Setup
root = tk.Tk()
root.title("Crusader Kings 2 Trainer")
root.geometry("400x400")

tk.Label(root, text="CK2 Ironman Trainer", font=("Arial", 14)).pack(pady=10)

tk.Label(root, text="This trainer will patch CK2 while the launcher is open. Start the game only after patching is complete.", 
         font=("Arial", 10), wraplength=380, justify="center").pack(pady=5)

monitor_button = tk.Button(root, text="Start Monitoring", command=start_monitoring)
monitor_button.pack(pady=5)

tk.Button(root, text="Stop Monitoring", command=stop_monitoring).pack(pady=5)
tk.Button(root, text="Exit", command=root.destroy).pack(pady=5)

tk.Label(root, text="Log:", font=("Arial", 10, "bold")).pack(anchor="w", padx=10)
log_text = tk.Text(root, height=8, width=50, state=tk.NORMAL)
log_text.pack(padx=10, pady=5, expand=True, fill="both")
root.mainloop()
