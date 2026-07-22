# Privacy Design and Notice Baseline

This document describes the default product behavior. A customer-specific privacy notice and data-processing terms must be approved before a paid deployment.

## Default behavior

Athmar Vision Count performs object detection on the device. The privacy-first release stores confirmed count records locally and does not store camera images or video. Network synchronization is disabled unless the customer expressly enables it and approves the endpoint, retention period, and operational purpose.

## Data handled

A confirmed count record may contain:

- a random scan session identifier;
- scan start and completion times;
- an operator reference chosen by the customer;
- a location or aisle reference chosen by the customer;
- SKU identifiers and display names;
- proposed and confirmed quantities;
- whether a quantity was manually adjusted.

The product must not request or store a stable advertising identifier, contact list, precise GPS location, biometric template, or unrelated device content.

## Camera processing

The camera is used only while the operator is actively scanning. Frames are processed for on-device inference and are not retained by the default release. Enabling image retention requires a separate documented purpose, customer approval, access controls, deletion controls, and legal review.

## Local storage and deletion

Confirmed sessions are stored in the application's private local storage. The application must expose a clear action to delete all locally stored scan records. Customer deployments must define and configure a retention period between 1 and 365 days.

## Optional synchronization

Synchronization is opt-in and must use an approved HTTPS endpoint. Before enabling it, document:

- the controller and processor roles;
- the fields transmitted;
- the lawful and contractual purpose;
- the retention and deletion process;
- access controls and audit logging;
- the support and incident-response contacts.

## Human review

The application proposes quantities. A human operator reviews and confirms every session before export or integration. The product must not silently update an inventory, ERP, accounting, or purchasing system.

## Customer responsibilities

The customer is responsible for authorized SKU data, staff instructions, device access control, physical counting procedures, approval of the privacy notice, and determining whether any additional notice or consent is required for its deployment.

## Publication gate

Replace this baseline with the approved public notice, set its version in the production AppConfig asset, and complete legal review before distributing a paid build.
