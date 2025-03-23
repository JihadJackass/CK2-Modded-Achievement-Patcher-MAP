import pymem
import pymem.process
import psutil
import time
import threading
import struct
import os
import sys
import re
import ctypes
import tkinter as tk
from tkinter import messagebox
from PIL import Image, ImageTk  # Ensure you have Pillow installed: pip install pillow

running = False
MEMORY_THRESHOLD_MB = 200
launcher_detected = False

# Required memory patches for enabling Ironman with Mods & Ruler Designer
aob_patterns = {
    "ironman": b"\x0F\xB6\x83\xF9\x02\x00\x00\x48",  # Ironman mode flag
    "ruler_designer": b"\xC6\x40\x62\x01\x48\x8B\x8B\x10\x6C\x00\x00",  # Ruler Designer check
    "savegame_check": b"\x88\x46\x61\x48\x8B\x03",  # Savegame-altered flag
    "checksum_check": b"\x88\x4F\x63\x48\x8B\x46\x30",  # Checksum (mod detection)
}

memory_patches = {
    "ironman": b"\xC6\x83\xF9\x02\x00\x00\x01",  # Force Ironman mode flag
    "ruler_designer": b"\xC6\x40\x62\x00\xC6\x40\x63\x01\xC6\x40\x65\x01",  # Disable RD flag, keep checksum valid
    "savegame_check": b"\x90\x90\x90",  # NOP the instruction that marks the save as altered
    "checksum_check": b"\x90\x90\x90",  # NOP the instruction that marks the checksum as invalid
}

def find_pattern(pm, module_base, module_size, pattern_bytes):
    """ Scans memory for the given pattern and returns the address while respecting wildcards """
    try:
        memory_dump = pm.read_bytes(module_base, module_size)

        # Convert `....` wildcards into regex equivalent (any 4 bytes)
        regex_pattern = re.escape(pattern_bytes).replace(b"....", b".{4}")

        # match memory patterns
        match = re.search(regex_pattern, memory_dump, re.DOTALL)
        if match:
            log_message(f"Found {pattern_bytes} at {hex(module_base + match.start())}")
            return module_base + match.start()
        # Log missing pattern
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

        # VirtualProtectEx to allow writing to memory
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

                    # Temporarily remove memory protection
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
                        log_message(f"ERROR: VirtualProtectEx failed for {key} at {hex(address)} - Write permission not granted!")
                        return
                    else:
                        log_message(f"Memory protection changed for {key} at {hex(address)}")

                    pm.write_bytes(address, patch_bytes, len(patch_bytes))

                    # Restore original protection
                    kernel32.VirtualProtectEx(
                        ctypes.c_void_p(pm.process_handle),
                        address_ptr,
                        ctypes.c_size_t(len(patch_bytes)),
                        old_protect.value,
                        ctypes.byref(old_protect)
                    )

                    log_message(f"Patched {key} at {hex(address)}")

                except Exception as e:
                    log_message(f"ERROR: Failed to write patch for {key} at {hex(address)} - {e}")

        log_message("Patching complete!")
        monitor_button.config(text="CK2game.exe Patched")

    except Exception as e:
        log_message(f"FATAL ERROR: {e}")
        messagebox.showerror("Error", f"Failed to apply memory patch: {e}")

def get_ck2_process():
    for proc in psutil.process_iter(attrs=['pid', 'name', 'memory_info']):
        if proc.info['name'].lower() == 'ck2game.exe':
            return proc.info['pid'], proc.info['memory_info'].rss // (1024 * 1024)
    return None, 0

def monitor_ck2():
    global running, launcher_detected
    monitor_button.config(text="Waiting for CK2 launcher...", state=tk.DISABLED)
    while running:
        pid, mem_usage = get_ck2_process()
        if pid:
            log_message("Detected CK2 launcher. Applying patch now...")
            patch_memory()
            return
        time.sleep(1)
    monitor_button.config(text="Start Patching", state=tk.NORMAL)

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
    monitor_button.config(text="Clear Patch", state=tk.NORMAL)
    log_message("Stopped patching attempt for CK2 launcher. You can use this again as long as it is during the launcher.")

def log_message(message):
    log_text.insert(tk.END, message + "\n")
    log_text.see(tk.END)

###########
#   GUI   #
###########

# Load and resize background image dynamically to fit the window
def update_bg(event=None):
    global bg_photo
    resized_image = bg_image.resize((root.winfo_width(), root.winfo_height()), Image.Resampling.LANCZOS)
    bg_photo = ImageTk.PhotoImage(resized_image)
    bg_label.config(image=bg_photo)

root = tk.Tk()
root.title("CK2 MAP | Made By JihadiJackass")
root.geometry("500x600")

# Set program icon
icon_path = os.path.join(os.path.abspath(os.path.dirname(__file__)), "ck2_icon.ico")
if os.path.exists(icon_path):
    root.iconbitmap(icon_path)
else:
    print("Warning: ck2_icon.ico not found. Skipping icon assignment.")

# Get the absolute path to the background image
if getattr(sys, 'frozen', False):
    bg_image_path = os.path.join(sys._MEIPASS, "background.png")  # PyInstaller temp folder
else:
    bg_image_path = os.path.join(os.path.abspath(os.path.dirname(__file__)), "background.png")

if not os.path.exists(bg_image_path):
    print("Warning: background.png not found. Check file path.")
else:
    bg_image = Image.open(bg_image_path)

# Open and resize the image
bg_image = Image.open(bg_image_path)
bg_image = bg_image.resize((500, 600), Image.LANCZOS)  # Resize to match window size

# Convert to a format Tkinter understands
bg_photo = ImageTk.PhotoImage(bg_image)

# Create the Label **after defining bg_photo**
bg_label = tk.Label(root, image=bg_photo)
bg_label.place(relwidth=1, relheight=1)  # Make it fill the entire window

# Update background on window resize
root.bind("<Configure>", update_bg)

# Ensure the title label is transparent and overlays the background properly
title_label = tk.Label(root, text="Crusader Kings 2 Modded Achievements Patcher", 
                       font=("Impact", 16), fg="gold", bg="#424478", relief="flat")
title_label.pack(pady=10)

# Instruction text
instructions = """
This tool allows earning achievements in modded CK2 games.

Instructions:
1. Start the CK2 launcher.
2. Apply the patch using this tool.
3. Start the game and play with mods in Ironman mode.
"""

instructions_label = tk.Label(root, text=instructions, font=("Times New Roman", 12), fg="white", bg="#2E3B60", wraplength=450, justify="center")
instructions_label.pack(pady=5)

def on_enter(e):
    e.widget.config(bg="#5456A0")  # Highlighted blue color

def on_leave(e):
    e.widget.config(bg="#424478")  # Default blue color

# Apply Patch Button
monitor_button = tk.Button(root, text="Apply Patch", command=patch_memory, font=("Arial", 12), bg="#424478", fg="white", relief="ridge")
monitor_button.pack(pady=5)
monitor_button.bind("<Enter>", on_enter)
monitor_button.bind("<Leave>", on_leave)

# Exit Button
exit_button = tk.Button(root, text="Exit", command=root.quit, font=("Arial", 12), bg="#424478", fg="white", relief="ridge")
exit_button.pack(pady=5)
exit_button.bind("<Enter>", on_enter)
exit_button.bind("<Leave>", on_leave)

# Logging UI
tk.Label(root, text="Log:", font=("Arial", 10, "bold"), bg="#3e2723", fg="white").pack(anchor="w", padx=10)
log_text = tk.Text(root, height=10, width=60, state=tk.NORMAL, bg="#2E3B60", fg="white", insertbackground="white")
log_text.pack(padx=10, pady=5, expand=True, fill="both")

root.mainloop()
