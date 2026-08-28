; Position-independent Windows x64 QPC wrapper. Code page followed by RW state.
; No game structures, fixed game addresses, DLLs, or external dependencies.
bits 64
default rel
org 0
state equ $$+4096

; BOOL QueryPerformanceCounter(LARGE_INTEGER* result)
; Only RBX is nonvolatile here. Shadow space and stack alignment follow Win64 ABI.
push rbx
sub rsp, 48
mov rbx, rcx
lea rcx, [rsp+32]
call qword [rel state+8]
test eax, eax
jz .return
mov r8, [rsp+32]
lea r11, [rel state]
.lock:
xor eax, eax
mov edx, 1
lock cmpxchg [r11+16], edx
jz .locked
pause
jmp .lock
.locked:
mov r9, [r11+24]             ; previous real counter
cmp r8, r9
cmovl r8, r9                ; calls can reach lock out of sample order
mov r10, [r11+56]           ; real-clock lease deadline
cmp r10, r9
cmovl r10, r9
cmp r10, r8
cmovg r10, r8               ; split elapsed interval exactly at lease expiry
mov rax, r10
sub rax, r9
imul rax, [r11+40]          ; previous active multiplier
add rax, [r11+32]
mov rdx, r8
sub rdx, r10
add rax, rdx                ; after expiry: real time only
mov rdx, [r11+48]           ; requested next multiplier
cmp r8, [r11+56]
jle .validate
mov edx, 1
xchg rdx, [r11+48]          ; timeout latches normal until explicit new request
cmp rdx, 1
je .expired
inc qword [r11+72]
.expired:
mov edx, 1
.validate:
cmp rdx, 1
je .commit
cmp rdx, 2
je .commit
cmp rdx, 3
je .commit
cmp rdx, 5
je .commit
cmp rdx, 10
je .commit
mov edx, 1
.commit:
mov [r11+24], r8
mov [r11+32], rax
mov [r11+40], rdx
inc qword [r11+64]
mov dword [r11+16], 0
mov [rbx], rax
mov eax, 1
.return:
add rsp, 48
pop rbx
ret

; Installer thread at offset 512. Atomic compare/exchange of the single IAT slot.
; RCX is the positively validated IAT address. Expected pointer is state.original.
times 512-($-$$) db 0xcc
mov rax, [rel state+8]
lea rdx, [rel $$]
lock cmpxchg [rcx], rdx
sete al
movzx eax, al
ret
