/// <reference path="../../typings/tsd.d.ts" />

import resource = require("models/resources/resource");
import changeSubscription = require("common/changeSubscription");
import changesCallback = require("common/changesCallback");
import EVENTS = require("common/constants/events");

import abstractWebSocketClient = require("common/abstractWebSocketClient");

abstract class abstractNotificationCenterClient extends abstractWebSocketClient<Raven.Server.NotificationCenter.Actions.Action> {

    constructor(rs: resource) {
        super(rs);
    }

    protected allReconnectHandlers = ko.observableArray<changesCallback<void>>();
    protected allAlertsHandlers = ko.observableArray<changesCallback<Raven.Server.NotificationCenter.Actions.AlertRaised>>();
    protected allOperationsHandlers = ko.observableArray<changesCallback<Raven.Server.NotificationCenter.Actions.OperationChanged>>();
    protected watchedOperationsChanged = new Map<number, KnockoutObservableArray<changesCallback<Raven.Server.NotificationCenter.Actions.OperationChanged>>>();

    //TODO: operation dismissed, operation postponed

    protected onOpen() {
        super.onOpen();
        this.connectToWebSocketTask.resolve();

        this.fireEvents<void>(this.allReconnectHandlers(), undefined, () => true);
    }

    protected onMessage(actionDto: Raven.Server.NotificationCenter.Actions.Action) {
        const actionType = actionDto.Type;

        switch (actionType) {
            case "AlertRaised":
                const alertDto = actionDto as Raven.Server.NotificationCenter.Actions.AlertRaised;
                this.fireEvents<Raven.Server.NotificationCenter.Actions.AlertRaised>(this.allAlertsHandlers(), alertDto, () => true);
                break;

            case "OperationChanged":
                const operationDto = actionDto as Raven.Server.NotificationCenter.Actions.OperationChanged;
                this.fireEvents<Raven.Server.NotificationCenter.Actions.OperationChanged>(this.allOperationsHandlers(), operationDto, () => true);

                this.watchedOperationsChanged.forEach((callbacks, key) => {
                    this.fireEvents<Raven.Server.NotificationCenter.Actions.OperationChanged>(callbacks(), operationDto, (event) => event.OperationId === key);
                });

                break;
            default: 
                super.onMessage(actionDto);
        }
    }

    watchReconnect(onChange: () => void) {
        const callback = new changesCallback<void>(onChange);

        this.allReconnectHandlers.push(callback);

        return new changeSubscription(() => {
            this.allReconnectHandlers.remove(callback);
        });
    }

    watchAllAlerts(onChange: (e: Raven.Server.NotificationCenter.Actions.AlertRaised) => void) {
        const callback = new changesCallback<Raven.Server.NotificationCenter.Actions.AlertRaised>(onChange);

        this.allAlertsHandlers.push(callback);

        return new changeSubscription(() => {
            this.allAlertsHandlers.remove(callback);
        });
    }

    watchAllOperations(onChange: (e: Raven.Server.NotificationCenter.Actions.OperationChanged) => void) {
        const callback = new changesCallback<Raven.Server.NotificationCenter.Actions.OperationChanged>(onChange);

        this.allOperationsHandlers.push(callback);

        return new changeSubscription(() => {
            this.allOperationsHandlers.remove(callback);
        });
    }

    watchOperation(operationId: number, onChange: (e: Raven.Server.NotificationCenter.Actions.OperationChanged) => void) {
        const callback = new changesCallback<Raven.Server.NotificationCenter.Actions.OperationChanged>(onChange);

        if (!this.watchedOperationsChanged.has(operationId)) {
            this.watchedOperationsChanged.set(operationId, ko.observableArray<changesCallback<Raven.Server.NotificationCenter.Actions.OperationChanged>>());
        }

        const callbacks = this.watchedOperationsChanged.get(operationId);
        callbacks.push(callback);

        return new changeSubscription(() => {
            callbacks.remove(callback);
            if (callbacks().length === 0) {
                this.watchedOperationsChanged.delete(operationId);
            }
        });
    }

}

export = abstractNotificationCenterClient;

