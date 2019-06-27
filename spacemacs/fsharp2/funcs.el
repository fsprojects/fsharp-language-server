;;; funcs.el --- fsharp2 Layer config File for Spacemacs
;;
;; Copyright (c) 2012-2018 Sylvain Benner & Contributors
;;
;; Author: Chris Marchetti <cam.marchetti@gmail.com>
;; URL: https://github.com/syl20bnr/spacemacs
;;
;; This file is not part of GNU Emacs.
;;
;;; License: GPLv3

(defun spacemacs//fsharp2-setup-intellisense ()
 "Conditionally enable fsharp-mode's built-in intellisense"
 (pcase fsharp2-backend
  (`lsp 
   (setq fsharp-ac-intellisense-enabled nil))
  (`fsac
   (setq fsharp-ac-intellisense-enabled t))))

(defun spacemacs//fsharp2-setup-backend ()
  "Conditionally setup fsharp backend"
 (pcase fsharp2-backend
  (`lsp
   (require 'lsp)
   ;; Required to avoid issues with lsp-mode's built-in F# client; even though 
   ;; we're using our mode instead, lsp-mode can't build the LSP client 
   ;; without this value defined
   (setq lsp-fsharp-server-path "")
   (lsp-register-client
    (make-lsp-client
     :new-connection (lsp-stdio-connection fsharp2-lsp-executable)
     :major-modes '(fsharp-mode)
     :server-id 'fsharp-lsp
     :notification-handlers (ht ("fsharp/startProgress" #'ignore)
                                ("fsharp/incrementProgress" #'ignore)
                                ("fsharp/endProgress" #'ignore))
     :priority 1))
   (lsp))))

(defun spacemacs//fsharp2-setup-bindings ()
 "Conditionally setup fsharp bindings" 
 (pcase fsharp2-backend 
  (`fsac 
   (spacemacs/declare-prefix-for-mode 'fsharp-mode "mf" "find")
   (spacemacs/declare-prefix-for-mode 'fsharp-mode "ms" "interpreter")
   (spacemacs/declare-prefix-for-mode 'fsharp-mode "mx" "executable")
   (spacemacs/declare-prefix-for-mode 'fsharp-mode "mc" "compile")
   (spacemacs/declare-prefix-for-mode 'fsharp-mode "mg" "goto")
   (spacemacs/declare-prefix-for-mode 'fsharp-mode "mh" "hint"))))

(defun spacemacs//fsharp2-setup-company ()
 "Conditionally setup company mode"
 (pcase fsharp2-backend
  (`lsp 
   (spacemacs|add-company-backends 
    :backends company-lsp 
    :modes fsharp-mode 
    :append-hooks nil 
    :call-hooks t)
   (company-mode))))
