export const createControllerChangeHandler = ({ getController, reload }) => {
    let hadController = getController() !== null;
    let reloaded = false;

    return () => {
        if (!hadController) {
            hadController = true;
            return false;
        }
        if (reloaded) {
            return false;
        }
        reloaded = true;
        reload();
        return true;
    };
};
