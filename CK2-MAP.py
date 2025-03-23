import pymem
import pymem.pattern
import psutil
import time
import threading
import tkinter as tk
from tkinter import messagebox

running = False
MEMORY_THRESHOLD_MB = 200

# Memory patches extracted from the Cheat Engine script
aob_patterns = {
    "console": b"\x48\x8B\xD9\x80\xB8\x00\x05\x00\x00\x00\x75",
    "enable_console": b"\x8B\x1D....\x4C\x39\xAB\xF0\x04\x00\x00",
    "uncorrupt_save": b"\xE8....\xC6\x40\x61\x00\x0F\xB6\x45\xA7\x88",
    "ironman": b"\x0F\xB6\x83\xF9\x02\x00\x00\x48",
    "ruler_designer": b"\xC6\x40\x62\x01\x48\x8B\x8B\x10\x6C\x00\x00",
    "savegame_check": b"\x88\x46\x61\x48\x8B\x03",
    "checksum_check": b"\x88\x4F\x63\x48\x8B\x46\x30",
}

memory_patches = {
    "console": b"\x90\x90\x90\x90\x90\x39\xC0",
    "enable_console": b"\x90\x90\x90\x90\x90\x39\xC0",
    "uncorrupt_save": b"\xC6\x40\x61\x01",
    "ironman": b"\xC6\x83\xF9\x02\x00\x00\x01",
    "ruler_designer": b"\xC6\x40\x62\x00\xC6\x40\x63\x01\xC6\x40\x65\x01",
}

def get_ck2_process():
    for proc in psutil.process_iter(attrs=['pid', 'name', 'memory_info']):
        if proc.info['name'].lower() == 'ck2game.exe':
            return proc.info['pid'], proc.info['memory_info'].rss // (1024 * 1024)
    return None, 0

def patch_memory():
    try:
        pm = pymem.Pymem("CK2game.exe")

        # Get a list of modules for CK2
        ck2_modules = list(pm.list_modules())

        # Find the CK2game.exe module
        module_base = None
        for module in ck2_modules:
            if "CK2game.exe" in module.name:
                module_base = module.lpBaseOfDll
                break

        if not module_base:
            log_message("Error: Could not find CK2game.exe module base address.")
            return

        log_message(f"CK2game.exe Base Address: {hex(module_base)}")

        # Apply memory patches
        for key, pattern in aob_patterns.items():
            # Scan memory for the pattern
            address = pymem.pattern.scan_pattern(pm.process_handle, module_base, pattern)
            
            if address:
                # Change memory protections before writing
                pm.write_bytes(address, memory_patches[key], len(memory_patches[key]))
                log_message(f"Patched {key} at {hex(address)}")
            else:
                log_message(f"Pattern not found for {key}")

        log_message("Patching complete!")
        monitor_button.config(text="CK2game.exe Patched")

    except Exception as e:
        log_message(f"Error: {e}")
        messagebox.showerror("Error", f"Failed to apply memory patch: {e}")
        
def monitor_ck2():
    global running
    monitor_button.config(text="Waiting for CK2game.exe...", state=tk.DISABLED)
    
    while running:
        pid, mem_usage = get_ck2_process()
        if pid:
            if mem_usage < MEMORY_THRESHOLD_MB:
                log_message("Detected CK2 launcher. Waiting for game to fully start...")
            else:
                log_message("Launcher closed. Waiting 3 seconds before applying patch...")
                time.sleep(3)
                log_message("CK2game.exe detected. Applying memory patch...")
                patch_memory()
                return
        
        time.sleep(1)
    monitor_button.config(text="Start Monitoring", state=tk.NORMAL)

def start_monitoring():
    global running
    if not running:
        running = True
        threading.Thread(target=monitor_ck2, daemon=True).start()
        log_message("Waiting for CK2game.exe to start...")
        monitor_button.config(text="Waiting for CK2game.exe...", state=tk.DISABLED)

def stop_monitoring():
    global running
    running = False
    monitor_button.config(text="Start Monitoring", state=tk.NORMAL)
    log_message("Stopped monitoring for CK2game.exe.")

def log_message(message):
    log_text.insert(tk.END, message + "\n")
    log_text.see(tk.END)

# GUI Setup
root = tk.Tk()
root.title("Crusader Kings 2 Trainer")
root.geometry("400x400")

tk.Label(root, text="CK2 Ironman Trainer", font=("Arial", 14)).pack(pady=10)

tk.Label(root, text="This trainer will monitor for CK2game.exe and apply modifications without affecting checksum.", 
         font=("Arial", 10), wraplength=380, justify="center").pack(pady=5)

monitor_button = tk.Button(root, text="Start Monitoring", command=start_monitoring)
monitor_button.pack(pady=5)

tk.Button(root, text="Stop Monitoring", command=stop_monitoring).pack(pady=5)
tk.Button(root, text="Exit", command=root.quit).pack(pady=5)

# Log Window
tk.Label(root, text="Log:", font=("Arial", 10, "bold")).pack(anchor="w", padx=10)
log_text = tk.Text(root, height=8, width=50, state=tk.NORMAL)
log_text.pack(padx=10, pady=5)

root.mainloop()
